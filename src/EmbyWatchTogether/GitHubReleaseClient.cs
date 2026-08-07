using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Downloads and verifies the one public release asset used by the plugin
    /// updater. The latest-version download URL is a plain GitHub web path, so
    /// checks do not consume the anonymous REST API rate limit that is shared
    /// with Emby's own update checks.
    /// </summary>
    public sealed class GitHubReleaseClient : IPluginReleaseClient
    {
        public const string RepositoryUrl = "https://github.com/hope140/EmbyWatchTogether";

        public const string ReleasePageUrl = RepositoryUrl + "/releases/latest";

        public const string LatestDownloadUrl =
            RepositoryUrl + "/releases/latest/download/Emby.Plugins.WatchTogether.dll";

        public const string AssetName = "Emby.Plugins.WatchTogether.dll";

        private const string ExpectedAssemblyName = "Emby.Plugins.WatchTogether";

        private readonly IHttpClient _httpClient;
        private readonly string _userAgent;

        public GitHubReleaseClient(
            IHttpClient httpClient,
            string userAgent = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _userAgent = string.IsNullOrWhiteSpace(userAgent)
                ? "EmbyWatchTogether/1.1 (+" + RepositoryUrl + ")"
                : userAgent;
        }

        public async Task<VerifiedPluginRelease> CheckForLatestAsync(CancellationToken cancellationToken)
        {
            if (!IsAllowedDownloadUrl(LatestDownloadUrl))
            {
                throw new ReleaseValidationException("正式版下载地址不是受信任的 GitHub release 地址。");
            }

            string tempFilePath = null;
            HttpResponseInfo response = null;
            try
            {
                var options = new HttpRequestOptions
                {
                    Url = LatestDownloadUrl,
                    UserAgent = _userAgent,
                    CancellationToken = cancellationToken,
                    ThrowOnErrorResponse = false,
                };

                response = await _httpClient.GetTempFileResponse(options).ConfigureAwait(false);
                tempFilePath = response?.TempFilePath;
                if (response == null || response.StatusCode != HttpStatusCode.OK ||
                    string.IsNullOrWhiteSpace(tempFilePath))
                {
                    throw new ReleaseValidationException("无法读取 GitHub 正式版信息。");
                }

                var fileInfo = new FileInfo(tempFilePath);
                if (!fileInfo.Exists || fileInfo.Length <= 0)
                {
                    throw new ReleaseValidationException("正式版插件 DLL 下载失败。");
                }

                string actualMd5;
                using (var stream = File.OpenRead(tempFilePath))
                using (var md5 = MD5.Create())
                {
                    actualMd5 = ToHex(md5.ComputeHash(stream));
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

                if (assemblyName == null ||
                    !string.Equals(assemblyName.Name, ExpectedAssemblyName, StringComparison.Ordinal))
                {
                    throw new ReleaseValidationException("正式版插件 DLL 程序集名称不匹配。");
                }

                if (assemblyName.Version == null)
                {
                    throw new ReleaseValidationException("正式版插件 DLL 程序集版本无效。");
                }

                var version = assemblyName.Version;
                return new VerifiedPluginRelease
                {
                    Release = new GitHubReleaseInfo
                    {
                        TagName = "v" + version,
                        HtmlUrl = ReleasePageUrl,
                        Draft = false,
                        Prerelease = false,
                        Version = version,
                        Assets = new System.Collections.Generic.List<GitHubReleaseAsset>
                        {
                            new GitHubReleaseAsset
                            {
                                Name = AssetName,
                                BrowserDownloadUrl = LatestDownloadUrl,
                                Size = fileInfo.Length,
                            },
                        },
                    },
                    Asset = new GitHubReleaseAsset
                    {
                        Name = AssetName,
                        BrowserDownloadUrl = LatestDownloadUrl,
                        Size = fileInfo.Length,
                    },
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
                        // Best-effort cleanup; the path is never returned to UI.
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

        public static bool IsAllowedDownloadUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var path = uri.AbsolutePath.TrimEnd('/');
            const string prefix = "/hope140/EmbyWatchTogether/releases/latest/download/";
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

        private static string ToHex(byte[] bytes)
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
