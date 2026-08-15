using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        [InlineData(GitHubReleaseClient.LatestDownloadUrl, true)]
        [InlineData(GitHubReleaseClient.LatestManifestDownloadUrl, true)]
        [InlineData(GitHubReleaseClient.LatestSignatureDownloadUrl, true)]
        [InlineData(GitHubReleaseClient.RepositoryUrl + "/releases/download/v1.2.3/" + GitHubReleaseClient.AssetName, true)]
        [InlineData(GitHubReleaseClient.RepositoryUrl + "/releases/download/v1.2.3/" + GitHubReleaseClient.ManifestAssetName, true)]
        [InlineData(GitHubReleaseClient.RepositoryUrl + "/releases/download/v1.2.3/" + GitHubReleaseClient.SignatureAssetName, true)]
        [InlineData("http://github.com/hope140/EmbyWatchTogether/releases/latest/download/Emby.Plugins.WatchTogether.dll", false)]
        [InlineData("https://github.com/other/repo/releases/latest/download/Emby.Plugins.WatchTogether.dll", false)]
        [InlineData("https://example.com/hope140/EmbyWatchTogether/releases/latest/download/Emby.Plugins.WatchTogether.dll", false)]
        [InlineData("https://github.com/hope140/EmbyWatchTogether/releases/latest/download/other.dll", false)]
        [InlineData(GitHubReleaseClient.LatestDownloadUrl + "?download=1", false)]
        [InlineData(GitHubReleaseClient.LatestDownloadUrl + "#asset", false)]
        [InlineData(GitHubReleaseClient.RepositoryUrl + "/releases/download/v1.2.3%2F4/" + GitHubReleaseClient.AssetName, false)]
        [InlineData(GitHubReleaseClient.RepositoryUrl + "/releases/download/v1.2.3/../" + GitHubReleaseClient.AssetName, false)]
        [InlineData(GitHubReleaseClient.RepositoryUrl + "/releases/download/v1.2.3/%2E%2E/" + GitHubReleaseClient.AssetName, false)]
        [InlineData(GitHubReleaseClient.RepositoryUrl + "/releases/download/v1.2/" + GitHubReleaseClient.AssetName, false)]
        [InlineData(GitHubReleaseClient.RepositoryUrl + "/releases/download/V1.2.3/" + GitHubReleaseClient.AssetName, false)]
        [InlineData(GitHubReleaseClient.RepositoryUrl + "/releases/download/v01.2.3/" + GitHubReleaseClient.AssetName, false)]
        [InlineData(GitHubReleaseClient.RepositoryUrl + "/releases/download/v1.2.3.4.5/" + GitHubReleaseClient.AssetName, false)]
        [InlineData(GitHubReleaseClient.RepositoryUrl + "/releases/download/v1.2.3/other.dll", false)]
        public void IsAllowedDownloadUrl_RestrictsCanonicalReleasePaths(string url, bool expected)
        {
            Assert.Equal(expected, GitHubReleaseClient.IsAllowedDownloadUrl(url));
        }

        [Fact]
        public async Task BetaCheck_SelectsHighestNonDraftPrereleaseAndUsesCanonicalAssets()
        {
            using (var fixture = SignedReleaseFixture.Create())
            {
                fixture.EnableBetaPaths();
                var releases = new List<GitHubReleaseApiDto>
                {
                    CreateApiRelease("v9.0.0", prerelease: false, draft: false),
                    CreateApiRelease("v8.0.0", prerelease: true, draft: true),
                    CreateApiRelease("v1.3.0.6", prerelease: true, draft: false),
                    CreateApiRelease("v1.3.0.5", prerelease: true, draft: false),
                };
                var client = CreateBetaClient(fixture, releases, out var requestedApiUrls);

                var verified = await client.CheckForLatestAsync(CancellationToken.None);

                Assert.Equal("v1.3.0.6", verified.Release.TagName);
                Assert.True(verified.Release.Prerelease);
                Assert.Equal(new[] { GitHubReleaseClient.ReleasesApiUrl }, requestedApiUrls);
                Assert.Equal(
                    new[]
                    {
                        GitHubReleaseClient.RepositoryUrl + "/releases/download/v1.3.0.6/" + GitHubReleaseClient.AssetName,
                        GitHubReleaseClient.RepositoryUrl + "/releases/download/v1.3.0.6/" + GitHubReleaseClient.ManifestAssetName,
                        GitHubReleaseClient.RepositoryUrl + "/releases/download/v1.3.0.6/" + GitHubReleaseClient.SignatureAssetName,
                    },
                    fixture.RequestedUrls);
                fixture.AssertReturnedFilesAreClean();
            }
        }

        [Fact]
        public async Task BetaCheck_MapsSnakeCaseApiJsonBeforeSelectingRelease()
        {
            using (var fixture = SignedReleaseFixture.Create())
            {
                fixture.EnableBetaPaths();
                var rawJson = "[{\"tag_name\":\"v1.3.0.6\",\"html_url\":\"https://example.invalid/release\",\"draft\":false,\"prerelease\":true,\"assets\":[" +
                    "{\"name\":\"Emby.Plugins.WatchTogether.dll\",\"browser_download_url\":\"https://example.invalid/dll\"}," +
                    "{\"name\":\"EmbyWatchTogether.release.manifest\"}," +
                    "{\"name\":\"EmbyWatchTogether.release.manifest.sig\"}]}]";
                var httpClient = new Mock<IHttpClient>();
                httpClient.Setup(x => x.GetResponse(It.IsAny<HttpRequestOptions>()))
                    .ReturnsAsync(new HttpResponseInfo
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new MemoryStream(Encoding.UTF8.GetBytes(rawJson)),
                    });
                httpClient.Setup(x => x.GetTempFileResponse(It.IsAny<HttpRequestOptions>()))
                    .Returns((HttpRequestOptions options) => Task.FromResult(fixture.CreateResponse(options.Url)));

                var serializer = new Mock<IJsonSerializer>();
                serializer.Setup(x => x.DeserializeFromString<List<GitHubReleaseApiDto>>(It.IsAny<string>()))
                    .Returns((string json) => JsonSerializer.Deserialize<List<GitHubReleaseApiDto>>(json));
                var client = new GitHubReleaseClient(
                    httpClient.Object,
                    signatureVerifier: fixture.Verifier,
                    jsonSerializer: serializer.Object,
                    updateChannel: PluginConfiguration.BetaUpdateChannel);

                var verified = await client.CheckForLatestAsync(CancellationToken.None);

                Assert.Equal("v1.3.0.6", verified.Release.TagName);
                Assert.Equal(GitHubReleaseClient.AssetName, verified.Asset.Name);
                serializer.Verify(x => x.DeserializeFromString<List<GitHubReleaseApiDto>>(rawJson), Times.Once);
                fixture.AssertReturnedFilesAreClean();
            }
        }

        [Theory]
        [InlineData("invalid")]
        [InlineData("v1.2")]
        public async Task BetaCheck_RejectsInvalidTag(string tag)
        {
            using (var fixture = SignedReleaseFixture.Create())
            {
                var client = CreateBetaClient(
                    fixture,
                    new List<GitHubReleaseApiDto> { CreateApiRelease(tag, prerelease: true, draft: false) },
                    out _);

                var exception = await Assert.ThrowsAsync<ReleaseValidationException>(() =>
                    client.CheckForLatestAsync(CancellationToken.None));

                Assert.Contains("测试版发布标签无效", exception.UserMessage);
                fixture.AssertReturnedFilesAreClean();
            }
        }

        [Fact]
        public async Task BetaCheck_RejectsMissingAssetAndInvalidJson()
        {
            using (var fixture = SignedReleaseFixture.Create())
            {
                var missingAsset = CreateApiRelease("v1.3.0.6", prerelease: true, draft: false);
                missingAsset.assets.RemoveAt(2);
                var client = CreateBetaClient(fixture, new List<GitHubReleaseApiDto> { missingAsset }, out _);

                var missingAssetException = await Assert.ThrowsAsync<ReleaseValidationException>(() =>
                    client.CheckForLatestAsync(CancellationToken.None));
                Assert.Contains("缺少固定资产", missingAssetException.UserMessage);
            }

            using (var fixture = SignedReleaseFixture.Create())
            {
                var client = CreateBetaClient(fixture, null, out _, invalidJson: true);

                var invalidJsonException = await Assert.ThrowsAsync<ReleaseValidationException>(() =>
                    client.CheckForLatestAsync(CancellationToken.None));
                Assert.Contains("API 响应无效", invalidJsonException.UserMessage);
            }
        }

        [Fact]
        public async Task BetaCheck_RejectsApiFailureAndNoBetaRelease()
        {
            using (var fixture = SignedReleaseFixture.Create())
            {
                var client = CreateBetaClient(
                    fixture,
                    new List<GitHubReleaseApiDto>(),
                    out _,
                    apiStatusCode: HttpStatusCode.ServiceUnavailable);

                var apiException = await Assert.ThrowsAsync<ReleaseValidationException>(() =>
                    client.CheckForLatestAsync(CancellationToken.None));
                Assert.Contains("API 请求失败", apiException.UserMessage);
            }

            using (var fixture = SignedReleaseFixture.Create())
            {
                var client = CreateBetaClient(
                    fixture,
                    new List<GitHubReleaseApiDto>(),
                    out _);

                var noReleaseException = await Assert.ThrowsAsync<ReleaseValidationException>(() =>
                    client.CheckForLatestAsync(CancellationToken.None));
                Assert.Contains("没有可用的测试版发布", noReleaseException.UserMessage);
            }
        }

        [Fact]
        public async Task BetaCheck_UsesSignedValidationAndCleansFiles()
        {
            using (var fixture = SignedReleaseFixture.Create())
            {
                fixture.EnableBetaPaths();
                fixture.Tamper("dll");
                var client = CreateBetaClient(
                    fixture,
                    new List<GitHubReleaseApiDto>
                    {
                        CreateApiRelease("v1.3.0.6", prerelease: true, draft: false),
                    },
                    out _);

                await Assert.ThrowsAsync<ReleaseValidationException>(() =>
                    client.CheckForLatestAsync(CancellationToken.None));

                fixture.AssertReturnedFilesAreClean();
            }
        }

        [Fact]
        public async Task BetaCheck_PropagatesCancellation()
        {
            using (var fixture = SignedReleaseFixture.Create())
            using (var cancellation = new CancellationTokenSource())
            {
                fixture.EnableBetaPaths();
                var client = CreateBetaClient(
                    fixture,
                    new List<GitHubReleaseApiDto>
                    {
                        CreateApiRelease("v1.3.0.6", prerelease: true, draft: false),
                    },
                    out _,
                    afterResponse: url =>
                    {
                        if (string.Equals(url, GitHubReleaseClient.RepositoryUrl + "/releases/download/v1.3.0.6/" + GitHubReleaseClient.ManifestAssetName, StringComparison.Ordinal))
                        {
                            cancellation.Cancel();
                        }
                    });

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    client.CheckForLatestAsync(cancellation.Token));
                fixture.AssertReturnedFilesAreClean();
            }
        }

        [Fact]
        public async Task CheckForLatestAsync_ValidatesSignedReleaseAndCleansAllTempFiles()
        {
            using (var fixture = SignedReleaseFixture.Create())
            {
                var client = CreateClient(fixture, out _);
                var verified = await client.CheckForLatestAsync(CancellationToken.None);
                var pluginVersion = typeof(Plugin).Assembly.GetName().Version;
                var expectedTag = "v" + pluginVersion;
                var expectedDownloadUrl = GitHubReleaseClient.RepositoryUrl +
                    "/releases/download/" + expectedTag + "/" + GitHubReleaseClient.AssetName;

                Assert.NotNull(verified);
                Assert.NotNull(verified.Release);
                Assert.NotNull(verified.Asset);
                Assert.Equal(pluginVersion, verified.Release.Version);
                Assert.Equal(expectedTag, verified.Release.TagName);
                Assert.Equal(GitHubReleaseClient.AssetName, verified.Asset.Name);
                Assert.Equal(expectedDownloadUrl, verified.Asset.BrowserDownloadUrl);
                Assert.NotEqual(GitHubReleaseClient.LatestDownloadUrl, verified.Asset.BrowserDownloadUrl);
                Assert.False(string.IsNullOrWhiteSpace(verified.Md5Checksum));
                Assert.NotEqual(fixture.DllPath, fixture.ManifestPath);
                Assert.NotEqual(fixture.DllPath, fixture.SignaturePath);
                Assert.NotEqual(fixture.ManifestPath, fixture.SignaturePath);
                Assert.Equal(
                    new[]
                    {
                        GitHubReleaseClient.LatestDownloadUrl,
                        GitHubReleaseClient.LatestManifestDownloadUrl,
                        GitHubReleaseClient.LatestSignatureDownloadUrl,
                    },
                    fixture.RequestedUrls);
                fixture.AssertReturnedFilesAreClean();
            }
        }

        [Theory]
        [InlineData("manifest")]
        [InlineData("signature")]
        [InlineData("dll")]
        public async Task CheckForLatestAsync_RejectsTamperedReleaseComponentAndCleansFiles(
            string component)
        {
            using (var fixture = SignedReleaseFixture.Create())
            {
                fixture.Tamper(component);
                var client = CreateClient(fixture, out _);

                await Assert.ThrowsAsync<ReleaseValidationException>(() =>
                    client.CheckForLatestAsync(CancellationToken.None));

                Assert.Equal(3, fixture.RequestedUrls.Count);
                fixture.AssertReturnedFilesAreClean();
            }
        }

        [Theory]
        [InlineData("dll", 403, false)]
        [InlineData("manifest", 404, false)]
        [InlineData("signature", 500, false)]
        [InlineData("dll", 200, true)]
        [InlineData("manifest", 200, true)]
        [InlineData("signature", 200, true)]
        public async Task CheckForLatestAsync_RejectsBadResponseOrMissingTempFileAndCleansAcquiredFiles(
            string failedComponent,
            int statusCode,
            bool missingTempFile)
        {
            using (var fixture = SignedReleaseFixture.Create())
            {
                fixture.ConfigureFailure(
                    failedComponent,
                    (HttpStatusCode)statusCode,
                    missingTempFile);
                var client = CreateClient(fixture, out _);

                await Assert.ThrowsAsync<ReleaseValidationException>(() =>
                    client.CheckForLatestAsync(CancellationToken.None));

                fixture.AssertReturnedFilesAreClean();
            }
        }

        [Fact]
        public async Task CheckForLatestAsync_RejectsNonPluginAssemblyAndCleansFiles()
        {
            using (var fixture = SignedReleaseFixture.Create())
            {
                fixture.UseAssetFile(typeof(GitHubReleaseClientTests).Assembly.Location);
                var client = CreateClient(fixture, out _);

                await Assert.ThrowsAsync<ReleaseValidationException>(() =>
                    client.CheckForLatestAsync(CancellationToken.None));

                fixture.AssertReturnedFilesAreClean();
            }
        }

        [Fact]
        public async Task CheckForLatestAsync_RejectsAssemblyVersionMismatchAndCleansFiles()
        {
            using (var fixture = SignedReleaseFixture.Create())
            {
                var pluginVersion = typeof(Plugin).Assembly.GetName().Version;
                var mismatchedVersion = new Version(
                    pluginVersion.Major,
                    pluginVersion.Minor,
                    pluginVersion.Build,
                    pluginVersion.Revision + 1);
                fixture.SetManifestVersion(mismatchedVersion.ToString());
                var client = CreateClient(fixture, out _);

                await Assert.ThrowsAsync<ReleaseValidationException>(() =>
                    client.CheckForLatestAsync(CancellationToken.None));

                fixture.AssertReturnedFilesAreClean();
            }
        }

        [Fact]
        public async Task CheckForLatestAsync_PropagatesCancellationAndCleansObtainedFiles()
        {
            using (var fixture = SignedReleaseFixture.Create())
            using (var cancellation = new CancellationTokenSource())
            {
                var client = CreateClient(
                    fixture,
                    out _,
                    url =>
                    {
                        if (string.Equals(
                                url,
                                GitHubReleaseClient.LatestManifestDownloadUrl,
                                StringComparison.Ordinal))
                        {
                            cancellation.Cancel();
                        }
                    });

                await Assert.ThrowsAsync<OperationCanceledException>(() =>
                    client.CheckForLatestAsync(cancellation.Token));

                Assert.Equal(
                    new[]
                    {
                        GitHubReleaseClient.LatestDownloadUrl,
                        GitHubReleaseClient.LatestManifestDownloadUrl,
                    },
                    fixture.RequestedUrls);
                fixture.AssertReturnedFilesAreClean();
            }
        }

        [Fact]
        public async Task CheckForLatestAsync_AllowsGitHubAssetCdnRedirects()
        {
            using (var fixture = SignedReleaseFixture.Create())
            {
                fixture.SetResponseUrl("https://objects.githubusercontent.com/github-production-release-asset");
                var client = CreateClient(fixture, out _);

                var verified = await client.CheckForLatestAsync(CancellationToken.None);

                Assert.NotNull(verified);
                fixture.AssertReturnedFilesAreClean();
            }
        }

        private static GitHubReleaseApiDto CreateApiRelease(string tag, bool prerelease, bool draft)
        {
            return new GitHubReleaseApiDto
            {
                tag_name = tag,
                draft = draft,
                prerelease = prerelease,
                assets = new List<GitHubReleaseAssetApiDto>
                {
                    new GitHubReleaseAssetApiDto { name = GitHubReleaseClient.AssetName },
                    new GitHubReleaseAssetApiDto { name = GitHubReleaseClient.ManifestAssetName },
                    new GitHubReleaseAssetApiDto { name = GitHubReleaseClient.SignatureAssetName },
                },
            };
        }

        private static GitHubReleaseClient CreateBetaClient(
            SignedReleaseFixture fixture,
            List<GitHubReleaseApiDto> releases,
            out List<string> requestedApiUrls,
            bool invalidJson = false,
            Action<string> afterResponse = null,
            HttpStatusCode apiStatusCode = HttpStatusCode.OK,
            string apiResponseUrl = null)
        {
            var apiUrls = new List<string>();
            requestedApiUrls = apiUrls;
            var httpClient = new Mock<IHttpClient>();
            httpClient.Setup(x => x.GetResponse(It.IsAny<HttpRequestOptions>()))
                .Returns((HttpRequestOptions options) =>
                {
                    apiUrls.Add(options.Url);
                    return Task.FromResult(new HttpResponseInfo
                    {
                        StatusCode = apiStatusCode,
                        ResponseUrl = apiResponseUrl,
                        Content = new MemoryStream(Encoding.UTF8.GetBytes("[]")),
                    });
                });
            httpClient.Setup(x => x.GetTempFileResponse(It.IsAny<HttpRequestOptions>()))
                .Returns((HttpRequestOptions options) =>
                {
                    Assert.NotNull(options.Progress);
                    var response = fixture.CreateResponse(options.Url);
                    afterResponse?.Invoke(options.Url);
                    return Task.FromResult(response);
                });

            var serializer = new Mock<IJsonSerializer>();
            if (invalidJson)
            {
                serializer.Setup(x => x.DeserializeFromString<List<GitHubReleaseApiDto>>(It.IsAny<string>()))
                    .Throws(new InvalidOperationException("invalid json"));
            }
            else
            {
                serializer.Setup(x => x.DeserializeFromString<List<GitHubReleaseApiDto>>(It.IsAny<string>()))
                    .Returns(releases);
            }

            return new GitHubReleaseClient(
                httpClient.Object,
                signatureVerifier: fixture.Verifier,
                jsonSerializer: serializer.Object,
                updateChannel: PluginConfiguration.BetaUpdateChannel);
        }

        private static GitHubReleaseClient CreateClient(
            SignedReleaseFixture fixture,
            out Mock<IHttpClient> httpClient,
            Action<string> afterResponse = null)
        {
            httpClient = new Mock<IHttpClient>();
            httpClient.Setup(x => x.GetTempFileResponse(It.IsAny<HttpRequestOptions>()))
                .Returns((HttpRequestOptions options) =>
                {
                    Assert.NotNull(options.Progress);
                    var response = fixture.CreateResponse(options.Url);
                    afterResponse?.Invoke(options.Url);
                    return Task.FromResult(response);
                });

            return new GitHubReleaseClient(
                httpClient.Object,
                signatureVerifier: fixture.Verifier);
        }

        private sealed class SignedReleaseFixture : IDisposable
        {
            private readonly RSA _signingKey;
            private readonly string _directory;
            private readonly Dictionary<string, string> _paths;
            private readonly List<string> _returnedPaths = new List<string>();
            private byte[] _assetBytes;
            private byte[] _manifestBytes;
            private byte[] _signatureBytes;
            private string _manifestVersion;
            private string _failedComponent;
            private HttpStatusCode _failureStatusCode = HttpStatusCode.OK;
            private bool _missingTempFile;
            private string _responseUrl;

            private SignedReleaseFixture()
            {
                _signingKey = RSA.Create(2048);
                _directory = Path.Combine(
                    Path.GetTempPath(),
                    "watchtogether-signed-release-" + Guid.NewGuid().ToString("N"));

                DllPath = Path.Combine(_directory, GitHubReleaseClient.AssetName);
                ManifestPath = Path.Combine(_directory, GitHubReleaseClient.ManifestAssetName);
                SignaturePath = Path.Combine(_directory, GitHubReleaseClient.SignatureAssetName);
                _paths = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [GitHubReleaseClient.LatestDownloadUrl] = DllPath,
                    [GitHubReleaseClient.LatestManifestDownloadUrl] = ManifestPath,
                    [GitHubReleaseClient.LatestSignatureDownloadUrl] = SignaturePath,
                };
                KeyId = "test-key-" + Guid.NewGuid().ToString("N");
                Verifier = new ReleaseSignatureVerifier(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [KeyId] = ToPublicKeyXml(_signingKey.ExportParameters(false)),
                    });
                _assetBytes = File.ReadAllBytes(typeof(Plugin).Assembly.Location);
                SetManifestVersion(typeof(Plugin).Assembly.GetName().Version.ToString());
            }

            public string DllPath { get; }

            public string ManifestPath { get; }

            public string SignaturePath { get; }

            public string KeyId { get; }

            public ReleaseSignatureVerifier Verifier { get; }

            public List<string> RequestedUrls { get; } = new List<string>();

            public static SignedReleaseFixture Create()
            {
                return new SignedReleaseFixture();
            }

            public HttpResponseInfo CreateResponse(string url)
            {
                if (!_paths.TryGetValue(url, out var path))
                {
                    throw new InvalidOperationException("Unexpected release URL: " + url);
                }

                RequestedUrls.Add(url);
                _returnedPaths.Add(path);
                var component = GetComponent(url);
                var isFailedComponent = string.Equals(
                    component,
                    _failedComponent,
                    StringComparison.Ordinal);
                if (!(isFailedComponent && _missingTempFile))
                {
                    Directory.CreateDirectory(_directory);
                    File.WriteAllBytes(path, GetComponentBytes(component));
                }

                return new HttpResponseInfo
                {
                    StatusCode = isFailedComponent ? _failureStatusCode : HttpStatusCode.OK,
                    ResponseUrl = _responseUrl,
                    TempFilePath = path,
                };
            }

            public void Tamper(string component)
            {
                switch (component)
                {
                    case "manifest":
                        _manifestBytes[_manifestBytes.Length - 1] =
                            _manifestBytes[_manifestBytes.Length - 1] == (byte)'0'
                                ? (byte)'1'
                                : (byte)'0';
                        break;
                    case "signature":
                        _signatureBytes[0] =
                            _signatureBytes[0] == (byte)'A' ? (byte)'B' : (byte)'A';
                        break;
                    case "dll":
                        _assetBytes[_assetBytes.Length - 1] ^= 0x01;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(component), component, null);
                }
            }

            public void ConfigureFailure(
                string component,
                HttpStatusCode statusCode,
                bool missingTempFile)
            {
                GetComponentPath(component);
                _failedComponent = component;
                _failureStatusCode = statusCode;
                _missingTempFile = missingTempFile;
            }

            public void SetResponseUrl(string responseUrl)
            {
                _responseUrl = responseUrl;
            }

            public void UseAssetFile(string path)
            {
                _assetBytes = File.ReadAllBytes(path);
                RebuildManifest();
            }

            public void SetManifestVersion(string version)
            {
                _manifestVersion = version;
                RebuildManifest();
            }

            public void EnableBetaPaths()
            {
                var tag = "v" + _manifestVersion;
                _paths[GitHubReleaseClient.RepositoryUrl + "/releases/download/" + tag + "/" + GitHubReleaseClient.AssetName] = DllPath;
                _paths[GitHubReleaseClient.RepositoryUrl + "/releases/download/" + tag + "/" + GitHubReleaseClient.ManifestAssetName] = ManifestPath;
                _paths[GitHubReleaseClient.RepositoryUrl + "/releases/download/" + tag + "/" + GitHubReleaseClient.SignatureAssetName] = SignaturePath;
            }

            public void AssertReturnedFilesAreClean()
            {
                foreach (var path in _returnedPaths)
                {
                    Assert.False(File.Exists(path), "Temporary file was not cleaned: " + path);
                }

                if (Directory.Exists(_directory))
                {
                    Assert.Empty(Directory.GetFiles(_directory));
                }
            }

            public void Dispose()
            {
                try
                {
                    _signingKey.Dispose();
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(_directory))
                        {
                            Directory.Delete(_directory, true);
                        }
                    }
                    catch
                    {
                        // Best-effort cleanup for test files.
                    }
                }
            }

            private void RebuildManifest()
            {
                var hash = ToLowerHex(ComputeSha256(_assetBytes));
                var manifestText = string.Join(
                    "\n",
                    "schema=1",
                    "keyId=" + KeyId,
                    "tag=v" + _manifestVersion,
                    "version=" + _manifestVersion,
                    "assetName=" + GitHubReleaseClient.AssetName,
                    "size=" + _assetBytes.Length,
                    "sha256=" + hash);
                _manifestBytes = new UTF8Encoding(false).GetBytes(manifestText);
                _signatureBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(
                    _signingKey.SignData(
                        _manifestBytes,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1)));
            }

            private byte[] GetComponentBytes(string component)
            {
                switch (component)
                {
                    case "dll":
                        return _assetBytes;
                    case "manifest":
                        return _manifestBytes;
                    case "signature":
                        return _signatureBytes;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(component), component, null);
                }
            }

            private string GetComponentPath(string component)
            {
                switch (component)
                {
                    case "dll":
                        return DllPath;
                    case "manifest":
                        return ManifestPath;
                    case "signature":
                        return SignaturePath;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(component), component, null);
                }
            }

            private static string GetComponent(string url)
            {
                if (url.EndsWith("/" + GitHubReleaseClient.AssetName, StringComparison.Ordinal))
                {
                    return "dll";
                }

                if (url.EndsWith("/" + GitHubReleaseClient.ManifestAssetName, StringComparison.Ordinal))
                {
                    return "manifest";
                }

                if (url.EndsWith("/" + GitHubReleaseClient.SignatureAssetName, StringComparison.Ordinal))
                {
                    return "signature";
                }

                throw new InvalidOperationException("Unexpected release URL: " + url);
            }

            private static byte[] ComputeSha256(byte[] bytes)
            {
                using (var sha256 = SHA256.Create())
                {
                    return sha256.ComputeHash(bytes);
                }
            }

            private static string ToPublicKeyXml(RSAParameters parameters)
            {
                return "<RSAKeyValue><Modulus>" +
                    Convert.ToBase64String(parameters.Modulus) +
                    "</Modulus><Exponent>" +
                    Convert.ToBase64String(parameters.Exponent) +
                    "</Exponent></RSAKeyValue>";
            }

            private static string ToLowerHex(byte[] bytes)
            {
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var value in bytes)
                {
                    builder.Append(value.ToString("x2"));
                }

                return builder.ToString();
            }
        }
    }
}
