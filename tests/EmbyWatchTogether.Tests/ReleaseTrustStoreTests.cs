using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Emby.Plugins.WatchTogether;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public sealed class ReleaseTrustStoreTests
    {
        [Fact]
        public void TrustedPublicKeys_DefaultsToAnEmptyReadOnlyMapping()
        {
            Assert.Empty(ReleaseTrustStore.TrustedPublicKeys);

            var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(
                ReleaseTrustStore.TrustedPublicKeys);
            Assert.Throws<NotSupportedException>(() => dictionary.Add("test-key", "test-key"));
            Assert.Empty(ReleaseTrustStore.TrustedPublicKeys);
        }

        [Fact]
        public void CreateVerifier_RejectsUnknownKeyId()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "watchtogether-trust-store-" + Guid.NewGuid().ToString("N"));
            var manifestPath = Path.Combine(directory, "release.manifest");
            var signaturePath = Path.Combine(directory, "release.manifest.sig");

            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(
                    manifestPath,
                    string.Join(
                        "\n",
                        "schema=1",
                        "keyId=unknown-key",
                        "tag=v1.2.3",
                        "version=1.2.3",
                        "assetName=" + GitHubReleaseClient.AssetName,
                        "size=0",
                        "sha256=" + new string('0', 64)),
                    new UTF8Encoding(false));
                File.WriteAllText(signaturePath, "AAAA", new UTF8Encoding(false));

                var exception = Assert.Throws<ReleaseValidationException>(() =>
                    ReleaseTrustStore.CreateVerifier().Verify(
                        manifestPath,
                        signaturePath,
                        Path.Combine(directory, GitHubReleaseClient.AssetName)));

                Assert.Equal("发布签名使用了未知的 keyId。", exception.UserMessage);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }
    }
}
