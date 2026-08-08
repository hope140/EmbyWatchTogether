using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Emby.Plugins.WatchTogether;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public sealed class ReleaseTrustStoreTests
    {
        [Fact]
        public void PublicKeys_ContainsReviewedProductionKeyAndIsReadOnly()
        {
            Assert.Single(ReleaseTrustStore.PublicKeys);
            Assert.True(ReleaseTrustStore.PublicKeys.ContainsKey("prod-2026-08"));
            var publicKeyXml = ReleaseTrustStore.PublicKeys["prod-2026-08"];
            Assert.False(string.IsNullOrWhiteSpace(publicKeyXml));
            var document = XDocument.Parse(publicKeyXml);
            Assert.Equal("RSAKeyValue", document.Root.Name.LocalName);
            Assert.False(string.IsNullOrWhiteSpace(document.Root.Element("Modulus")?.Value));
            Assert.Equal("AQAB", document.Root.Element("Exponent")?.Value);

            var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(
                ReleaseTrustStore.PublicKeys);
            Assert.Throws<NotSupportedException>(() => dictionary.Add("test-key", "test-key"));
            Assert.Single(ReleaseTrustStore.PublicKeys);
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
