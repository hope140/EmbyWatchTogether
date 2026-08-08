using System;
using System.IO;
using Emby.Plugins.WatchTogether;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public sealed class ReleaseTrustStoreTests
    {
        [Fact]
        public void PublicKeys_StartEmptyAndAreReadOnly()
        {
            Assert.Empty(ReleaseTrustStore.PublicKeys);
            Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyDictionary<string, string>>(
                ReleaseTrustStore.PublicKeys);
        }

        [Fact]
        public void CreateVerifier_FailsClosedForUnknownKey()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "watchtogether-empty-trust-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var manifestPath = Path.Combine(directory, "release.manifest");
            var signaturePath = Path.Combine(directory, "release.manifest.sig");
            var assetPath = Path.Combine(directory, GitHubReleaseClient.AssetName);
            try
            {
                File.WriteAllText(
                    manifestPath,
                    "schema=1\n" +
                    "keyId=untrusted\n" +
                    "tag=v1.2.3\n" +
                    "version=1.2.3\n" +
                    "assetName=" + GitHubReleaseClient.AssetName + "\n" +
                    "size=0\n" +
                    "sha256=" + new string('0', 64));
                File.WriteAllText(signaturePath, "AAAA");
                File.WriteAllBytes(assetPath, new byte[0]);

                Assert.Throws<ReleaseValidationException>(() =>
                    ReleaseTrustStore.CreateVerifier().Verify(
                        manifestPath,
                        signaturePath,
                        assetPath));
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
