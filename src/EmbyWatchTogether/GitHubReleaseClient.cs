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

        public const string ManifestAssetName = "EmbyWatchTogether.release.manifest";

        public const string SignatureAssetName = "EmbyWatchTogether.release.manifest.sig";

        public const string LatestManifestDownloadUrl =
            RepositoryUrl + "/releases/latest/download/" + ManifestAssetName;

        public const string LatestSignatureDownloadUrl =
            RepositoryUrl + "/releases/latest/download/" + SignatureAssetName;

        private const string ExpectedAssemblyName = "Emby.Plugins.WatchTogether";

        private readonly IHttpClient _httpClient;
        private readonly ReleaseSignatureVerifier _signatureVerifier;
        private readonly string _userAgent;

        public GitHubReleaseClient(
            IHttpClient httpClient,
            string userAgent = null,
            ReleaseSignatureVerifier signatureVerifier = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _signatureVerifier = signatureVerifier ?? ReleaseTrustStore.CreateVerifier();
            _userAgent = string.IsNullOrWhiteSpace(userAgent)
                ? "EmbyWatchTogether/1.1 (+" + RepositoryUrl + ")"
                : userAgent;
        }

        public async Task<VerifiedPluginRelease> CheckForLatestAsync(CancellationToken cancellationToken)
        {
            string dllPath = null;
            string manifestPath = null;
            string signaturePath = null;
            HttpResponseInfo dllResponse = null;
            HttpResponseInfo manifestResponse = null;
            HttpResponseInfo signatureResponse = null;
            try
            {
                if (!IsAllowedDownloadUrl(LatestDownloadUrl) ||
                    !IsAllowedDownloadUrl(LatestManifestDownloadUrl) ||
                    !IsAllowedDownloadUrl(LatestSignatureDownloadUrl))
                {
                    throw new ReleaseValidationException("正式版下载地址不是受信任的 GitHub release 地址。");
                }

                dllResponse = await GetTempFileResponseAsync(
                    LatestDownloadUrl,
                    cancellationToken).ConfigureAwait(false);
                dllPath = dllResponse?.TempFilePath;
                var dllSize = ValidateDownloadedFile(
                    dllResponse,
                    dllPath,
                    ReleaseSignatureVerifier.MaxAssetBytes,
                    "正式版插件 DLL");

                manifestResponse = await GetTempFileResponseAsync(
                    LatestManifestDownloadUrl,
                    cancellationToken).ConfigureAwait(false);
                manifestPath = manifestResponse?.TempFilePath;
                ValidateDownloadedFile(
                    manifestResponse,
                    manifestPath,
                    ReleaseSignatureVerifier.MaxManifestBytes,
                    "正式版发布清单");

                signatureResponse = await GetTempFileResponseAsync(
                    LatestSignatureDownloadUrl,
                    cancellationToken).ConfigureAwait(false);
                signaturePath = signatureResponse?.TempFilePath;
                ValidateDownloadedFile(
                    signatureResponse,
                    signaturePath,
                    ReleaseSignatureVerifier.MaxSignatureBytes,
                    "正式版发布签名");

                cancellationToken.ThrowIfCancellationRequested();

                ReleaseManifest manifest;
                try
                {
                    manifest = _signatureVerifier.Verify(manifestPath, signaturePath, dllPath);
                }
                catch (ReleaseValidationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new ReleaseValidationException("正式版发布清单校验失败。", ex);
                }

                if (manifest == null ||
                    !TryParseReleaseVersion(manifest.Version, out var manifestVersion) ||
                    !IsCanonicalReleaseTag(manifest.Tag) ||
                    !string.Equals(manifest.Tag, "v" + manifest.Version, StringComparison.Ordinal))
                {
                    throw new ReleaseValidationException("正式版发布清单版本无效。");
                }

                AssemblyName assemblyName;
                try
                {
                    assemblyName = AssemblyName.GetAssemblyName(dllPath);
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

                if (!VersionsEqual(assemblyName.Version, manifestVersion))
                {
                    throw new ReleaseValidationException("正式版插件 DLL 程序集版本与发布清单不一致。");
                }

                var tagDownloadUrl = CreateTagDownloadUrl(manifest.Tag, AssetName);
                if (!IsAllowedDownloadUrl(tagDownloadUrl))
                {
                    throw new ReleaseValidationException("正式版下载地址不是受信任的 GitHub release 地址。");
                }

                var releasePageUrl = CreateTagReleasePageUrl(manifest.Tag);
                var actualMd5 = CalculateMd5(dllPath);
                return new VerifiedPluginRelease
                {
                    Release = new GitHubReleaseInfo
                    {
                        TagName = manifest.Tag,
                        HtmlUrl = releasePageUrl,
                        Draft = false,
                        Prerelease = false,
                        Version = manifestVersion,
                        Assets = new System.Collections.Generic.List<GitHubReleaseAsset>
                        {
                            new GitHubReleaseAsset
                            {
                                Name = AssetName,
                                BrowserDownloadUrl = tagDownloadUrl,
                                Size = dllSize,
                            },
                        },
                    },
                    Asset = new GitHubReleaseAsset
                    {
                        Name = AssetName,
                        BrowserDownloadUrl = tagDownloadUrl,
                        Size = dllSize,
                    },
                    Md5Checksum = actualMd5,
                };
            }
            finally
            {
                CleanupDownloadedFile(dllResponse, dllPath);
                CleanupDownloadedFile(manifestResponse, manifestPath);
                CleanupDownloadedFile(signatureResponse, signaturePath);
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
            if (string.IsNullOrWhiteSpace(url) ||
                !string.Equals(url, url.Trim(), StringComparison.Ordinal) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !uri.IsDefaultPort ||
                uri.Query.Length != 0 ||
                uri.Fragment.Length != 0 ||
                !TryGetRawPath(url, out var path))
            {
                return false;
            }

            if (path.IndexOf('\\') >= 0 || path.IndexOf('%') >= 0)
            {
                return false;
            }

            if (string.Equals(path, GetLatestDownloadPath(AssetName), StringComparison.Ordinal) ||
                string.Equals(path, GetLatestDownloadPath(ManifestAssetName), StringComparison.Ordinal) ||
                string.Equals(path, GetLatestDownloadPath(SignatureAssetName), StringComparison.Ordinal))
            {
                return true;
            }

            const string tagPrefix = "/hope140/EmbyWatchTogether/releases/download/";
            if (!path.StartsWith(tagPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var tagAndAsset = path.Substring(tagPrefix.Length);
            var separator = tagAndAsset.IndexOf('/');
            if (separator <= 0 || separator != tagAndAsset.LastIndexOf('/'))
            {
                return false;
            }

            var tag = tagAndAsset.Substring(0, separator);
            var assetName = tagAndAsset.Substring(separator + 1);
            return IsCanonicalReleaseTag(tag) && IsFixedAssetName(assetName);
        }

        private async Task<HttpResponseInfo> GetTempFileResponseAsync(
            string url,
            CancellationToken cancellationToken)
        {
            if (!IsAllowedDownloadUrl(url))
            {
                throw new ReleaseValidationException("正式版下载地址不是受信任的 GitHub release 地址。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var options = new HttpRequestOptions
            {
                Url = url,
                UserAgent = _userAgent,
                CancellationToken = cancellationToken,
                ThrowOnErrorResponse = false,
                Progress = new Progress<double>(),
            };

            return await _httpClient.GetTempFileResponse(options).ConfigureAwait(false);
        }

        private static long ValidateDownloadedFile(
            HttpResponseInfo response,
            string tempFilePath,
            long maxBytes,
            string description)
        {
            if (response == null || response.StatusCode != HttpStatusCode.OK ||
                string.IsNullOrWhiteSpace(tempFilePath))
            {
                throw new ReleaseValidationException(description + "下载失败。");
            }

            try
            {
                var fileInfo = new FileInfo(tempFilePath);
                if (!fileInfo.Exists)
                {
                    throw new ReleaseValidationException(description + "临时文件不存在。");
                }

                if (fileInfo.Length > maxBytes)
                {
                    throw new ReleaseValidationException(description + "超过大小限制。");
                }

                return fileInfo.Length;
            }
            catch (ReleaseValidationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ReleaseValidationException(description + "临时文件校验失败。", ex);
            }
        }

        private static string CalculateMd5(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                using (var md5 = MD5.Create())
                {
                    return ToHex(md5.ComputeHash(stream));
                }
            }
            catch (Exception ex)
            {
                throw new ReleaseValidationException("正式版插件 DLL MD5 校验失败。", ex);
            }
        }

        private static void CleanupDownloadedFile(
            HttpResponseInfo response,
            string tempFilePath)
        {
            try
            {
                response?.Dispose();
            }
            catch
            {
                // Best-effort cleanup; every downloaded response is disposable.
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(tempFilePath) && File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch
            {
                // Best-effort cleanup; no temporary path is returned to the caller.
            }
        }

        private static string CreateTagDownloadUrl(string tag, string assetName)
        {
            return RepositoryUrl + "/releases/download/" + tag + "/" + assetName;
        }

        private static string CreateTagReleasePageUrl(string tag)
        {
            return RepositoryUrl + "/releases/tag/" + tag;
        }

        private static string GetLatestDownloadPath(string assetName)
        {
            return "/hope140/EmbyWatchTogether/releases/latest/download/" + assetName;
        }

        private static bool TryGetRawPath(string url, out string path)
        {
            path = null;
            var schemeSeparator = url.IndexOf("://", StringComparison.Ordinal);
            if (schemeSeparator < 0)
            {
                return false;
            }

            var authorityStart = schemeSeparator + 3;
            var pathStart = url.IndexOfAny(new[] { '/', '?', '#' }, authorityStart);
            if (pathStart < 0 || url[pathStart] != '/')
            {
                return false;
            }

            if (url.IndexOfAny(new[] { '?', '#' }, pathStart) >= 0)
            {
                return false;
            }

            path = url.Substring(pathStart);
            return path.Length > 0;
        }

        private static bool IsFixedAssetName(string assetName)
        {
            return string.Equals(assetName, AssetName, StringComparison.Ordinal) ||
                string.Equals(assetName, ManifestAssetName, StringComparison.Ordinal) ||
                string.Equals(assetName, SignatureAssetName, StringComparison.Ordinal);
        }

        private static bool IsCanonicalReleaseTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || !tag.StartsWith("v", StringComparison.Ordinal))
            {
                return false;
            }

            var versionText = tag.Substring(1);
            var parts = versionText.Split('.');
            if (parts.Length < 3 || parts.Length > 4)
            {
                return false;
            }

            foreach (var part in parts)
            {
                if (part.Length == 0 || (part.Length > 1 && part[0] == '0'))
                {
                    return false;
                }

                foreach (var character in part)
                {
                    if (character < '0' || character > '9')
                    {
                        return false;
                    }
                }
            }

            return Version.TryParse(versionText, out var version) &&
                string.Equals(tag, "v" + version, StringComparison.Ordinal);
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
