using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Serialization;
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
        [InlineData("https://github.com/hope140/EmbyWatchTogether/releases/download/v1.0.0/Emby.Plugins.WatchTogether.dll", true)]
        [InlineData("http://github.com/hope140/EmbyWatchTogether/releases/download/v1.0.0/Emby.Plugins.WatchTogether.dll", false)]
        [InlineData("https://github.com/other/repo/releases/download/v1.0.0/Emby.Plugins.WatchTogether.dll", false)]
        [InlineData("https://example.com/hope140/EmbyWatchTogether/releases/download/v1.0.0/Emby.Plugins.WatchTogether.dll", false)]
        [InlineData("https://github.com/hope140/EmbyWatchTogether/releases/download/v1.0.0/other.dll", false)]
        public void IsAllowedAssetUrl_RestrictsHttpsRepositoryPath(string url, bool expected)
        {
            Assert.Equal(expected, GitHubReleaseClient.IsAllowedAssetUrl(url));
        }

        [Fact]
        public async Task DownloadAndVerifyAsync_RejectsDraftAndPrerelease()
        {
            foreach (var flags in new[] { (draft: true, prerelease: false), (draft: false, prerelease: true) })
            {
                var client = CreateClient(out _);
                var release = CreateRelease(flags.draft, flags.prerelease);

                await Assert.ThrowsAsync<ReleaseValidationException>(() =>
                    client.DownloadAndVerifyAsync(release, CancellationToken.None));
            }
        }

        [Fact]
        public async Task DownloadAndVerifyAsync_ValidatesSizeDigestAssemblyAndCleansTempFile()
        {
            var client = CreateClient(out var httpClient);
            var tempPath = CopyPluginAssembly();
            var bytes = File.ReadAllBytes(tempPath);
            var release = CreateRelease(false, false);
            release.Assets[0].Size = bytes.LongLength;
            release.Assets[0].Digest = "sha256:" + Sha256(bytes);
            httpClient.Setup(x => x.GetTempFileResponse(It.IsAny<HttpRequestOptions>()))
                .ReturnsAsync(new HttpResponseInfo { StatusCode = HttpStatusCode.OK, TempFilePath = tempPath });

            var verified = await client.DownloadAndVerifyAsync(release, CancellationToken.None);

            Assert.NotNull(verified);
            Assert.False(string.IsNullOrWhiteSpace(verified.Md5Checksum));
            Assert.False(File.Exists(tempPath));
        }

        [Fact]
        public async Task DownloadAndVerifyAsync_RejectsWrongAssetUrlDigestOrSize()
        {
            var cases = new[] { "url", "digest", "size" };
            foreach (var failure in cases)
            {
                var client = CreateClient(out var httpClient);
                var tempPath = CopyPluginAssembly();
                var bytes = File.ReadAllBytes(tempPath);
                var release = CreateRelease(false, false);
                release.Assets[0].Size = failure == "size" ? bytes.LongLength + 1 : bytes.LongLength;
                release.Assets[0].Digest = failure == "digest" ? "sha256:" + new string('0', 64) : "sha256:" + Sha256(bytes);
                if (failure == "url")
                {
                    release.Assets[0].BrowserDownloadUrl = "https://example.com/plugin.dll";
                }

                httpClient.Setup(x => x.GetTempFileResponse(It.IsAny<HttpRequestOptions>()))
                    .ReturnsAsync(new HttpResponseInfo { StatusCode = HttpStatusCode.OK, TempFilePath = tempPath });

                await Assert.ThrowsAsync<ReleaseValidationException>(() =>
                    client.DownloadAndVerifyAsync(release, CancellationToken.None));

                // The client only owns the temporary file after the download
                // attempt starts. The untrusted-URL case is rejected before any
                // download, so the file fabricated by this test is left alone.
                if (failure != "url")
                {
                    Assert.False(File.Exists(tempPath));
                }
            }
        }

        [Fact]
        public async Task DownloadAndVerifyAsync_RejectsAssemblyVersionMismatch()
        {
            var client = CreateClient(out var httpClient);
            var tempPath = CopyPluginAssembly();
            var bytes = File.ReadAllBytes(tempPath);
            var release = CreateRelease(false, false);
            release.Version = new Version(9, 9, 9);
            release.Assets[0].Size = bytes.LongLength;
            release.Assets[0].Digest = "sha256:" + Sha256(bytes);
            httpClient.Setup(x => x.GetTempFileResponse(It.IsAny<HttpRequestOptions>()))
                .ReturnsAsync(new HttpResponseInfo { StatusCode = HttpStatusCode.OK, TempFilePath = tempPath });

            await Assert.ThrowsAsync<ReleaseValidationException>(() =>
                client.DownloadAndVerifyAsync(release, CancellationToken.None));
            Assert.False(File.Exists(tempPath));
        }

        private static GitHubReleaseClient CreateClient(out Mock<IHttpClient> httpClient)
        {
            httpClient = new Mock<IHttpClient>();
            return new GitHubReleaseClient(httpClient.Object, new Mock<IJsonSerializer>().Object);
        }

        private static GitHubReleaseInfo CreateRelease(bool draft, bool prerelease)
        {
            var version = typeof(Plugin).Assembly.GetName().Version ?? new Version(1, 0, 0);
            return new GitHubReleaseInfo
            {
                TagName = "v" + version,
                Version = version,
                HtmlUrl = "https://github.com/hope140/EmbyWatchTogether/releases/tag/v" + version,
                Draft = draft,
                Prerelease = prerelease,
                Assets = new System.Collections.Generic.List<GitHubReleaseAsset>
                {
                    new GitHubReleaseAsset
                    {
                        Name = GitHubReleaseClient.AssetName,
                        BrowserDownloadUrl = "https://github.com/hope140/EmbyWatchTogether/releases/download/v" + version + "/Emby.Plugins.WatchTogether.dll",
                    },
                },
            };
        }

        private static string CopyPluginAssembly()
        {
            var path = Path.Combine(Path.GetTempPath(), "watchtogether-test-" + Guid.NewGuid().ToString("N") + ".dll");
            File.Copy(typeof(Plugin).Assembly.Location, path);
            return path;
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
