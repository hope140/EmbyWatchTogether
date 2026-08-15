using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// The small subset of the GitHub release response that is needed by the
    /// updater. Keeping this model independent from the JSON serializer makes
    /// the validation rules easy to exercise without network access.
    /// </summary>
    public sealed class GitHubReleaseInfo
    {
        public string TagName { get; set; }

        public string HtmlUrl { get; set; }

        public bool Draft { get; set; }

        public bool Prerelease { get; set; }

        public List<GitHubReleaseAsset> Assets { get; set; } = new List<GitHubReleaseAsset>();

        public Version Version { get; set; }
    }

    public sealed class GitHubReleaseAsset
    {
        public string Name { get; set; }

        public string BrowserDownloadUrl { get; set; }

        public long Size { get; set; }

        public string Digest { get; set; }
    }

    /// <summary>
    /// GitHub's Releases API uses snake_case. Keep that wire format explicit
    /// instead of relying on serializer naming conventions.
    /// </summary>
    public sealed class GitHubReleaseApiDto
    {
        public string tag_name { get; set; }

        public string html_url { get; set; }

        public bool draft { get; set; }

        public bool prerelease { get; set; }

        public List<GitHubReleaseAssetApiDto> assets { get; set; }

        public GitHubReleaseInfo ToReleaseInfo()
        {
            var release = new GitHubReleaseInfo
            {
                TagName = tag_name,
                HtmlUrl = html_url,
                Draft = draft,
                Prerelease = prerelease,
                Assets = new List<GitHubReleaseAsset>(),
            };

            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    release.Assets.Add(asset?.ToAsset());
                }
            }

            return release;
        }
    }

    public sealed class GitHubReleaseAssetApiDto
    {
        public string name { get; set; }

        public string browser_download_url { get; set; }

        public long size { get; set; }

        public string digest { get; set; }

        public GitHubReleaseAsset ToAsset()
        {
            return new GitHubReleaseAsset
            {
                Name = name,
                BrowserDownloadUrl = browser_download_url,
                Size = size,
                Digest = digest,
            };
        }
    }

    /// <summary>
    /// A release after the fixed DLL has been downloaded and independently
    /// verified. The installer receives the MD5 checksum for Emby's own
    /// validation pass.
    /// </summary>
    public sealed class VerifiedPluginRelease
    {
        public GitHubReleaseInfo Release { get; set; }

        public GitHubReleaseAsset Asset { get; set; }

        public string Md5Checksum { get; set; }
    }

    public interface IPluginReleaseClient
    {
        /// <summary>
        /// Downloads and verifies the selected release asset in one pass.
        /// The concrete client selects stable or beta according to its
        /// normalized channel configuration.
        /// </summary>
        Task<VerifiedPluginRelease> CheckForLatestAsync(
            CancellationToken cancellationToken);
    }

    public sealed class PluginUpdateStatus
    {
        public string CurrentVersion { get; set; }

        public string LatestVersion { get; set; }

        public bool UpdateAvailable { get; set; }

        public bool IsChecking { get; set; }

        public bool IsInstalling { get; set; }

        public bool StartingUp { get; set; }

        public bool RestartRequired { get; set; }

        public string PendingVersion { get; set; }

        public string PendingUpdateVersion
        {
            get => PendingVersion;
            set => PendingVersion = value;
        }

        public DateTimeOffset? LastCheckedAtUtc { get; set; }

        public string LastError { get; set; }

        public string ReleaseUrl { get; set; }

        public string RepositoryUrl { get; set; }

        public PluginUpdateStatus Clone()
        {
            return new PluginUpdateStatus
            {
                CurrentVersion = CurrentVersion,
                LatestVersion = LatestVersion,
                UpdateAvailable = UpdateAvailable,
                IsChecking = IsChecking,
                IsInstalling = IsInstalling,
                StartingUp = StartingUp,
                RestartRequired = RestartRequired,
                PendingVersion = PendingVersion,
                LastCheckedAtUtc = LastCheckedAtUtc,
                LastError = LastError,
                ReleaseUrl = ReleaseUrl,
                RepositoryUrl = RepositoryUrl,
            };
        }
    }

    /// <summary>
    /// A validation failure is deliberately separate from transport errors so
    /// callers can show a concise, actionable message while logging the full
    /// exception privately.
    /// </summary>
    public sealed class ReleaseValidationException : Exception
    {
        public ReleaseValidationException(string userMessage)
            : base(userMessage)
        {
            UserMessage = userMessage;
        }

        public ReleaseValidationException(string userMessage, Exception innerException)
            : base(userMessage, innerException)
        {
            UserMessage = userMessage;
        }

        public string UserMessage { get; }
    }
}
