using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Reads and verifies the one public GitHub release used by the plugin
    /// updater. No other network endpoint is consulted.
    /// </summary>
    public sealed class GitHubReleaseClient : IPluginReleaseClient
    {
        public const string RepositoryUrl = "https://github.com/hope140/EmbyWatchTogether";

        public const string LatestReleaseApiUrl =
            "https://api.github.com/repos/hope140/EmbyWatchTogether/releases/latest";

        public const string AssetName = "Emby.Plugins.WatchTogether.dll";

        private const string GitHubAcceptHeader = "application/vnd.github+json";
        private const string ExpectedAssemblyName = "Emby.Plugins.WatchTogether";

        private readonly IHttpClient _httpClient;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly string _userAgent;

        public GitHubReleaseClient(
            IHttpClient httpClient,
            IJsonSerializer jsonSerializer,
            string userAgent = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
            _userAgent = string.IsNullOrWhiteSpace(userAgent)
                ? "EmbyWatchTogether/1.0 (+https://github.com/hope140/EmbyWatchTogether)"
                : userAgent;
        }

        public async Task<GitHubReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
        {
            var options = new HttpRequestOptions
            {
                Url = LatestReleaseApiUrl,
                AcceptHeader = GitHubAcceptHeader,
                UserAgent = _userAgent,
                CancellationToken = cancellationToken,
                ThrowOnErrorResponse = false,
            };

            HttpResponseInfo response = null;
            try
            {
                response = await _httpClient.GetResponse(options).ConfigureAwait(false);
                if (response == null || response.StatusCode != HttpStatusCode.OK || response.Content == null)
                {
                    throw new ReleaseValidationException("无法读取 GitHub 正式版信息。");
                }

                using (response.Content)
                using (var reader = new StreamReader(response.Content, Encoding.UTF8, true, 4096, true))
                {
                    var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                    var payload = _jsonSerializer.DeserializeFromString<GitHubReleasePayload>(body);
                    return ConvertRelease(payload);
                }
            }
            finally
            {
                response?.Dispose();
            }
        }

        public async Task<VerifiedPluginRelease> DownloadAndVerifyAsync(
            GitHubReleaseInfo release,
            CancellationToken cancellationToken)
        {
            if (release == null)
            {
                throw new ReleaseValidationException("GitHub 正式版信息为空。");
            }

            EnsureReleaseMetadata(release);

            var asset = (release.Assets ?? Enumerable.Empty<GitHubReleaseAsset>())
                .SingleOrDefault(a => a != null && string.Equals(a.Name, AssetName, StringComparison.Ordinal));
            if (asset == null)
            {
                throw new ReleaseValidationException("正式版缺少固定名称的插件 DLL。");
            }

            if (!IsAllowedAssetUrl(asset.BrowserDownloadUrl))
            {
                throw new ReleaseValidationException("正式版下载地址不是受信任的 GitHub release 地址。");
            }

            var expectedSha256 = ParseSha256Digest(asset.Digest);
            if (expectedSha256 == null)
            {
                throw new ReleaseValidationException("正式版缺少 SHA-256 校验摘要。");
            }

            string tempFilePath = null;
            HttpResponseInfo response = null;
            try
            {
                var options = new HttpRequestOptions
                {
                    Url = asset.BrowserDownloadUrl,
                    AcceptHeader = "application/octet-stream",
                    UserAgent = _userAgent,
                    CancellationToken = cancellationToken,
                    ThrowOnErrorResponse = false,
                };

                response = await _httpClient.GetTempFileResponse(options).ConfigureAwait(false);
                tempFilePath = response?.TempFilePath;
                if (response == null || response.StatusCode != HttpStatusCode.OK || string.IsNullOrWhiteSpace(tempFilePath))
                {
                    throw new ReleaseValidationException("正式版插件 DLL 下载失败。");
                }

                var fileInfo = new FileInfo(tempFilePath);
                if (!fileInfo.Exists || asset.Size < 0 || fileInfo.Length != asset.Size)
                {
                    throw new ReleaseValidationException("正式版插件 DLL 大小校验失败。");
                }

                string actualSha256;
                string actualMd5;
                using (var stream = File.OpenRead(tempFilePath))
                using (var sha256 = SHA256.Create())
                using (var md5 = MD5.Create())
                {
                    var sha256Bytes = sha256.ComputeHash(stream);
                    stream.Position = 0;
                    var md5Bytes = md5.ComputeHash(stream);
                    actualSha256 = ToHex(sha256Bytes);
                    actualMd5 = ToHex(md5Bytes);
                }

                if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ReleaseValidationException("正式版插件 DLL SHA-256 校验失败。");
                }

                AssemblyName assemblyName;
                try
                {
                    assemblyName = AssemblyName.GetAssemblyName(tempFilePath);
                }
                catch (Exception ex)
                {
                    throw new ReleaseValidationException("正式版插件 DLL 程序集校验失败。", ex);
                }

                if (assemblyName == null || !string.Equals(assemblyName.Name, ExpectedAssemblyName, StringComparison.Ordinal))
                {
                    throw new ReleaseValidationException("正式版插件 DLL 程序集名称不匹配。");
                }

                if (!VersionsEqual(assemblyName.Version, release.Version))
                {
                    throw new ReleaseValidationException("正式版插件 DLL 程序集版本与 tag 不一致。");
                }

                return new VerifiedPluginRelease
                {
                    Release = release,
                    Asset = asset,
                    Md5Checksum = actualMd5,
                };
            }
            finally
            {
                response?.Dispose();
                if (!string.IsNullOrWhiteSpace(tempFilePath))
                {
                    try
                    {
                        if (File.Exists(tempFilePath))
                        {
                            File.Delete(tempFilePath);
                        }
                    }
                    catch
                    {
                        // A best-effort cleanup is preferable to masking the
                        // validation result. The path is never returned to UI.
                    }
                }
            }
        }

        public static bool TryParseReleaseVersion(string tagName, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return false;
            }

            var value = tagName.Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(1);
            }

            return Version.TryParse(value, out version);
        }

        public static bool IsAllowedAssetUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var path = uri.AbsolutePath.TrimEnd('/');
            const string prefix = "/hope140/EmbyWatchTogether/releases/download/";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith("/" + AssetName, StringComparison.Ordinal);
        }

        public static bool VersionsEqual(Version left, Version right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return left.Major == right.Major &&
                left.Minor == right.Minor &&
                NormalizeVersionPart(left.Build) == NormalizeVersionPart(right.Build) &&
                NormalizeVersionPart(left.Revision) == NormalizeVersionPart(right.Revision);
        }

        private static int NormalizeVersionPart(int value)
        {
            return value < 0 ? 0 : value;
        }

        private static GitHubReleaseInfo ConvertRelease(GitHubReleasePayload payload)
        {
            if (payload == null)
            {
                throw new ReleaseValidationException("GitHub 正式版信息格式无效。");
            }

            if (!TryParseReleaseVersion(payload.tag_name, out var version))
            {
                throw new ReleaseValidationException("GitHub 正式版 tag 不是有效版本号。");
            }

            var release = new GitHubReleaseInfo
            {
                TagName = payload.tag_name,
                HtmlUrl = payload.html_url,
                Draft = payload.draft,
                Prerelease = payload.prerelease,
                Version = version,
                Assets = (payload.assets ?? new GitHubAssetPayload[0])
                    .Select(a => new GitHubReleaseAsset
                    {
                        Name = a?.name,
                        BrowserDownloadUrl = a?.browser_download_url,
                        Size = a?.size ?? -1,
                        Digest = a?.digest,
                    })
                    .ToList(),
            };

            EnsureReleaseMetadata(release);
            return release;
        }

        private static void EnsureReleaseMetadata(GitHubReleaseInfo release)
        {
            if (release.Draft || release.Prerelease)
            {
                throw new ReleaseValidationException("GitHub 当前版本不是正式版。");
            }

            if (release.Version == null && !TryParseReleaseVersion(release.TagName, out var version))
            {
                throw new ReleaseValidationException("GitHub 正式版 tag 不是有效版本号。");
            }

            if (release.Version == null)
            {
                TryParseReleaseVersion(release.TagName, out var parsedVersion);
                release.Version = parsedVersion;
            }
        }

        private static string ParseSha256Digest(string digest)
        {
            if (string.IsNullOrWhiteSpace(digest))
            {
                return null;
            }

            var value = digest.Trim();
            var separator = value.IndexOf(':');
            if (separator <= 0 || !string.Equals(value.Substring(0, separator), "sha256", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var hex = value.Substring(separator + 1).Trim();
            if (hex.Length != 64 || hex.Any(c => !Uri.IsHexDigit(c)))
            {
                return null;
            }

            return hex.ToLowerInvariant();
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }

        private sealed class GitHubReleasePayload
        {
            public string tag_name { get; set; }

            public string html_url { get; set; }

            public bool draft { get; set; }

            public bool prerelease { get; set; }

            public GitHubAssetPayload[] assets { get; set; }
        }

        private sealed class GitHubAssetPayload
        {
            public string name { get; set; }

            public string browser_download_url { get; set; }

            public long? size { get; set; }

            public string digest { get; set; }
        }
    }
}
