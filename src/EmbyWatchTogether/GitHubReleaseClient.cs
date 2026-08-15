using System;
using System.Collections.Generic;
using System.IO;
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
    /// Downloads and verifies the signed release assets used by the plugin
    /// updater. Stable checks use fixed latest-download paths; beta checks
    /// first select a release from the public GitHub API.
    /// </summary>
    public sealed class GitHubReleaseClient : IPluginReleaseClient
    {
        public const string RepositoryUrl = "https://github.com/hope140/EmbyWatchTogether";

        public const string ReleasePageUrl = RepositoryUrl + "/releases/latest";

        public const string ReleasesApiUrl =
            "https://api.github.com/repos/hope140/EmbyWatchTogether/releases?per_page=100&page=1";

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

        private const int MaxApiResponseBytes = 1024 * 1024;

        private readonly IHttpClient _httpClient;
        private readonly ReleaseSignatureVerifier _signatureVerifier;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly string _userAgent;
        private readonly string _updateChannel;

        public GitHubReleaseClient(
            IHttpClient httpClient,
            string userAgent = null,
            ReleaseSignatureVerifier signatureVerifier = null,
            IJsonSerializer jsonSerializer = null,
            string updateChannel = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _signatureVerifier = signatureVerifier ?? ReleaseTrustStore.CreateVerifier();
            _jsonSerializer = jsonSerializer;
            _userAgent = string.IsNullOrWhiteSpace(userAgent)
                ? "EmbyWatchTogether/1.1 (+" + RepositoryUrl + ")"
                : userAgent;
            _updateChannel = PluginConfiguration.NormalizeUpdateChannel(updateChannel);
        }

        public async Task<VerifiedPluginRelease> CheckForLatestAsync(CancellationToken cancellationToken)
        {
            if (string.Equals(_updateChannel, PluginConfiguration.BetaUpdateChannel, StringComparison.Ordinal))
            {
                return await CheckForBetaAsync(cancellationToken).ConfigureAwait(false);
            }

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

        private async Task<VerifiedPluginRelease> CheckForBetaAsync(
            CancellationToken cancellationToken)
        {
            var release = await GetLatestBetaReleaseAsync(cancellationToken).ConfigureAwait(false);
            var dllUrl = CreateTagDownloadUrl(release.TagName, AssetName);
            var manifestUrl = CreateTagDownloadUrl(release.TagName, ManifestAssetName);
            var signatureUrl = CreateTagDownloadUrl(release.TagName, SignatureAssetName);
            string dllPath = null;
            string manifestPath = null;
            string signaturePath = null;
            HttpResponseInfo dllResponse = null;
            HttpResponseInfo manifestResponse = null;
            HttpResponseInfo signatureResponse = null;
            try
            {
                dllResponse = await GetTempFileResponseAsync(dllUrl, "测试版", cancellationToken)
                    .ConfigureAwait(false);
                dllPath = dllResponse?.TempFilePath;
                var dllSize = ValidateDownloadedFile(
                    dllResponse,
                    dllPath,
                    ReleaseSignatureVerifier.MaxAssetBytes,
                    "测试版插件 DLL");

                manifestResponse = await GetTempFileResponseAsync(manifestUrl, "测试版", cancellationToken)
                    .ConfigureAwait(false);
                manifestPath = manifestResponse?.TempFilePath;
                ValidateDownloadedFile(
                    manifestResponse,
                    manifestPath,
                    ReleaseSignatureVerifier.MaxManifestBytes,
                    "测试版发布清单");

                signatureResponse = await GetTempFileResponseAsync(signatureUrl, "测试版", cancellationToken)
                    .ConfigureAwait(false);
                signaturePath = signatureResponse?.TempFilePath;
                ValidateDownloadedFile(
                    signatureResponse,
                    signaturePath,
                    ReleaseSignatureVerifier.MaxSignatureBytes,
                    "测试版发布签名");

                cancellationToken.ThrowIfCancellationRequested();
                ReleaseManifest manifest;
                try
                {
                    manifest = _signatureVerifier.Verify(manifestPath, signaturePath, dllPath);
                }
                catch (ReleaseValidationException)
                {
                    throw new ReleaseValidationException("测试版发布清单校验失败。");
                }
                catch (Exception ex)
                {
                    throw new ReleaseValidationException("测试版发布清单校验失败。", ex);
                }

                if (manifest == null ||
                    !TryParseReleaseVersion(manifest.Version, out var manifestVersion) ||
                    !IsCanonicalReleaseTag(manifest.Tag) ||
                    !string.Equals(manifest.Tag, "v" + manifest.Version, StringComparison.Ordinal) ||
                    !string.Equals(manifest.Tag, release.TagName, StringComparison.Ordinal) ||
                    !VersionsEqual(manifestVersion, release.Version))
                {
                    throw new ReleaseValidationException("测试版发布清单版本无效。");
                }

                AssemblyName assemblyName;
                try
                {
                    assemblyName = AssemblyName.GetAssemblyName(dllPath);
                }
                catch (Exception ex)
                {
                    throw new ReleaseValidationException("测试版插件 DLL 程序集校验失败。", ex);
                }

                if (assemblyName == null ||
                    !string.Equals(assemblyName.Name, ExpectedAssemblyName, StringComparison.Ordinal))
                {
                    throw new ReleaseValidationException("测试版插件 DLL 程序集名称不匹配。");
                }

                if (assemblyName.Version == null || !VersionsEqual(assemblyName.Version, manifestVersion))
                {
                    throw new ReleaseValidationException("测试版插件 DLL 程序集版本与发布清单不一致。");
                }

                string md5Checksum;
                try
                {
                    md5Checksum = CalculateMd5(dllPath);
                }
                catch (ReleaseValidationException ex)
                {
                    throw new ReleaseValidationException("测试版插件 DLL MD5 校验失败。", ex);
                }

                return new VerifiedPluginRelease
                {
                    Release = release,
                    Asset = new GitHubReleaseAsset
                    {
                        Name = AssetName,
                        BrowserDownloadUrl = dllUrl,
                        Size = dllSize,
                    },
                    Md5Checksum = md5Checksum,
                };
            }
            finally
            {
                CleanupDownloadedFile(dllResponse, dllPath);
                CleanupDownloadedFile(manifestResponse, manifestPath);
                CleanupDownloadedFile(signatureResponse, signaturePath);
            }
        }

        private async Task<GitHubReleaseInfo> GetLatestBetaReleaseAsync(
            CancellationToken cancellationToken)
        {
            if (_jsonSerializer == null)
            {
                throw new ReleaseValidationException("测试版 JSON 解析器不可用。");
            }

            HttpResponseInfo response = null;
            try
            {
                response = await _httpClient.GetResponse(new HttpRequestOptions
                {
                    Url = ReleasesApiUrl,
                    AcceptHeader = "application/vnd.github+json",
                    UserAgent = _userAgent,
                    CancellationToken = cancellationToken,
                    ThrowOnErrorResponse = false,
                    Progress = new Progress<double>(),
                }).ConfigureAwait(false);

                if (response == null || response.StatusCode != HttpStatusCode.OK ||
                    (response.ContentLength.HasValue && response.ContentLength.Value > MaxApiResponseBytes))
                {
                    throw new ReleaseValidationException("测试版 Releases API 请求失败。");
                }

                if (!string.IsNullOrWhiteSpace(response.ResponseUrl) &&
                    !string.Equals(response.ResponseUrl, ReleasesApiUrl, StringComparison.Ordinal))
                {
                    throw new ReleaseValidationException("测试版 Releases API 重定向地址不受信任。");
                }

                if (response.Content == null)
                {
                    throw new ReleaseValidationException("测试版 Releases API 响应无效。");
                }

                using (var stream = response.Content)
                using (var memory = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    var total = 0;
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                               .ConfigureAwait(false)) > 0)
                    {
                        total += read;
                        if (total > MaxApiResponseBytes)
                        {
                            throw new ReleaseValidationException("测试版 Releases API 响应超过大小限制。");
                        }

                        memory.Write(buffer, 0, read);
                    }

                    var json = Encoding.UTF8.GetString(memory.ToArray());
                    List<GitHubReleaseApiDto> apiReleases;
                    try
                    {
                        apiReleases = _jsonSerializer.DeserializeFromString<List<GitHubReleaseApiDto>>(json);
                    }
                    catch (Exception ex)
                    {
                        throw new ReleaseValidationException("测试版 Releases API 响应无效。", ex);
                    }

                    if (apiReleases == null)
                    {
                        throw new ReleaseValidationException("测试版 Releases API 响应无效。");
                    }

                    GitHubReleaseInfo selected = null;
                    foreach (var apiRelease in apiReleases)
                    {
                        var release = apiRelease?.ToReleaseInfo();
                        if (release == null || release.Draft || !release.Prerelease)
                        {
                            continue;
                        }

                        if (!IsCanonicalReleaseTag(release.TagName) ||
                            !TryParseReleaseVersion(release.TagName, out var version))
                        {
                            throw new ReleaseValidationException("测试版发布标签无效。");
                        }

                        if (!HasRequiredAssets(release))
                        {
                            throw new ReleaseValidationException("测试版发布缺少固定资产。");
                        }

                        release.Version = version;
                        if (selected == null || CompareVersions(version, selected.Version) > 0)
                        {
                            selected = release;
                        }
                    }

                    if (selected == null)
                    {
                        throw new ReleaseValidationException("没有可用的测试版发布。");
                    }

                    selected.HtmlUrl = CreateTagReleasePageUrl(selected.TagName);
                    foreach (var asset in selected.Assets)
                    {
                        if (asset == null)
                        {
                            continue;
                        }

                        if (string.Equals(asset.Name, AssetName, StringComparison.Ordinal))
                        {
                            asset.BrowserDownloadUrl = CreateTagDownloadUrl(selected.TagName, AssetName);
                        }
                        else if (string.Equals(asset.Name, ManifestAssetName, StringComparison.Ordinal))
                        {
                            asset.BrowserDownloadUrl = CreateTagDownloadUrl(selected.TagName, ManifestAssetName);
                        }
                        else if (string.Equals(asset.Name, SignatureAssetName, StringComparison.Ordinal))
                        {
                            asset.BrowserDownloadUrl = CreateTagDownloadUrl(selected.TagName, SignatureAssetName);
                        }
                    }

                    return selected;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ReleaseValidationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ReleaseValidationException("测试版 Releases API 请求失败。", ex);
            }
            finally
            {
                try
                {
                    response?.Dispose();
                }
                catch
                {
                    // Best-effort cleanup; no API response is retained.
                }
            }
        }

        private static bool HasRequiredAssets(GitHubReleaseInfo release)
        {
            if (release == null || release.Assets == null)
            {
                return false;
            }

            return HasAsset(release.Assets, AssetName) &&
                HasAsset(release.Assets, ManifestAssetName) &&
                HasAsset(release.Assets, SignatureAssetName);
        }

        private static bool HasAsset(IEnumerable<GitHubReleaseAsset> assets, string name)
        {
            foreach (var asset in assets)
            {
                if (asset != null && string.Equals(asset.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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
            return await GetTempFileResponseAsync(url, "正式版", cancellationToken).ConfigureAwait(false);
        }

        private async Task<HttpResponseInfo> GetTempFileResponseAsync(
            string url,
            string channelLabel,
            CancellationToken cancellationToken)
        {
            if (!IsAllowedDownloadUrl(url))
            {
                throw new ReleaseValidationException(channelLabel + "下载地址不是受信任的 GitHub release 地址。");
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

        public static bool IsCanonicalReleaseTag(string tag)
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

        private static int CompareVersions(Version left, Version right)
        {
            if (left == null)
            {
                return right == null ? 0 : -1;
            }

            if (right == null)
            {
                return 1;
            }

            var comparison = left.Major.CompareTo(right.Major);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Minor.CompareTo(right.Minor);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = NormalizeVersionPart(left.Build).CompareTo(NormalizeVersionPart(right.Build));
            if (comparison != 0)
            {
                return comparison;
            }

            return NormalizeVersionPart(left.Revision).CompareTo(NormalizeVersionPart(right.Revision));
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
