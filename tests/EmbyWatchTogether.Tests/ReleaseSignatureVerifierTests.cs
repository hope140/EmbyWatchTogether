using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Emby.Plugins.WatchTogether;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public sealed class ReleaseSignatureVerifierTests
    {
        [Fact]
        public void Verify_AcceptsSignedManifestAndMatchingAsset()
        {
            using (var fixture = SignedFixture.Create())
            {
                var manifest = fixture.Verifier.Verify(
                    fixture.ManifestPath,
                    fixture.SignaturePath,
                    fixture.AssetPath);

                Assert.Equal("release-key-1", manifest.KeyId);
                Assert.Equal("v1.2.3.4", manifest.Tag);
                Assert.Equal("1.2.3.4", manifest.Version);
                Assert.Equal(GitHubReleaseClient.AssetName, manifest.AssetName);
                Assert.Equal(fixture.AssetBytes.Length, manifest.Size);
            }
        }

        [Fact]
        public void Verify_RejectsTamperedManifest()
        {
            using (var fixture = SignedFixture.Create())
            {
                fixture.WriteManifest(fixture.ManifestText.Replace(
                    fixture.ManifestSha256,
                    new string('0', 64)));

                Assert.Throws<ReleaseValidationException>(() => fixture.Verify());
            }
        }

        [Fact]
        public void Verify_RejectsTamperedAsset()
        {
            using (var fixture = SignedFixture.Create())
            {
                fixture.WriteAsset(new byte[] { 1, 2, 3, 4, 6 });

                Assert.Throws<ReleaseValidationException>(() => fixture.Verify());
            }
        }

        [Fact]
        public void Verify_RejectsUnknownKeyId()
        {
            using (var fixture = SignedFixture.Create(keyId: "not-trusted"))
            {
                fixture.Verifier = new ReleaseSignatureVerifier(
                    new Dictionary<string, string>(StringComparer.Ordinal));

                Assert.Throws<ReleaseValidationException>(() => fixture.Verify());
            }
        }

        [Fact]
        public void Verify_RejectsInvalidSignature()
        {
            using (var fixture = SignedFixture.Create())
            {
                fixture.WriteSignature(new byte[] { 1, 2, 3, 4 });

                Assert.Throws<ReleaseValidationException>(() => fixture.Verify());
            }
        }

        [Theory]
        [InlineData("schema=1\nschema=1\ntag=v1.2.3.4\nversion=1.2.3.4\nassetName=Emby.Plugins.WatchTogether.dll\nsize=5\nsha256=0000000000000000000000000000000000000000000000000000000000000000")]
        [InlineData("schema=1\nkeyId=release-key-1\nunknown=value\nversion=1.2.3.4\nassetName=Emby.Plugins.WatchTogether.dll\nsize=5\nsha256=0000000000000000000000000000000000000000000000000000000000000000")]
        [InlineData("schema=1\nkeyId=release-key-1\ntag=v1.2.3.4\nversion=1.2.3.4\nassetName=Emby.Plugins.WatchTogether.dll\nsize=5")]
        public void Verify_RejectsDuplicateUnknownOrMissingFields(string manifest)
        {
            using (var fixture = SignedFixture.Create())
            {
                fixture.WriteManifest(manifest);

                Assert.Throws<ReleaseValidationException>(() => fixture.Verify());
            }
        }

        [Fact]
        public void Verify_RejectsBomCrLfAndExtraWhitespace()
        {
            using (var fixture = SignedFixture.Create())
            {
                fixture.WriteManifestBytes(Prepend(Encoding.UTF8.GetPreamble(), fixture.ManifestBytes));
                Assert.Throws<ReleaseValidationException>(() => fixture.Verify());

                fixture.WriteManifest(fixture.ManifestText.Replace("\n", "\r\n"));
                Assert.Throws<ReleaseValidationException>(() => fixture.Verify());

                fixture.WriteManifest(fixture.ManifestText.Replace("schema=1", "schema =1"));
                Assert.Throws<ReleaseValidationException>(() => fixture.Verify());
            }
        }

        [Theory]
        [InlineData("assetName=..\\Emby.Plugins.WatchTogether.dll")]
        [InlineData("assetName=other.dll")]
        public void Verify_RejectsPathOrUnexpectedAssetName(string replacement)
        {
            using (var fixture = SignedFixture.Create())
            {
                fixture.WriteManifest(fixture.ManifestText.Replace(
                    "assetName=" + GitHubReleaseClient.AssetName,
                    replacement));

                Assert.Throws<ReleaseValidationException>(() => fixture.Verify());
            }
        }

        [Fact]
        public void Verify_RejectsSizeOrHashMismatch()
        {
            using (var fixture = SignedFixture.Create())
            {
                fixture.WriteManifest(fixture.ManifestText.Replace("size=5", "size=6"));
                Assert.Throws<ReleaseValidationException>(() => fixture.Verify());
            }

            using (var fixture = SignedFixture.Create())
            {
                fixture.WriteManifest(fixture.ManifestText.Replace(
                    fixture.ManifestSha256,
                    new string('0', 64)));
                Assert.Throws<ReleaseValidationException>(() => fixture.Verify());
            }
        }

        [Fact]
        public void Verify_RejectsFilesAboveConfiguredLimits()
        {
            using (var fixture = SignedFixture.Create())
            {
                fixture.WriteManifest(fixture.ManifestText + "\n" + new string('x', 17 * 1024));
                Assert.Throws<ReleaseValidationException>(() => fixture.Verify());
            }

            using (var fixture = SignedFixture.Create())
            {
                fixture.WriteSignature(new byte[(int)ReleaseSignatureVerifier.MaxSignatureBytes + 1]);
                Assert.Throws<ReleaseValidationException>(() => fixture.Verify());
            }
        }

        [Fact]
        public void Verify_RejectsAssetAboveConfiguredLimitBeforeHashing()
        {
            using (var fixture = SignedFixture.Create())
            {
                using (var stream = new FileStream(
                           fixture.AssetPath,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.SetLength(ReleaseSignatureVerifier.MaxAssetBytes + 1);
                }

                Assert.Throws<ReleaseValidationException>(() => fixture.Verify());
            }
        }

        private static byte[] Prepend(byte[] prefix, byte[] value)
        {
            var result = new byte[prefix.Length + value.Length];
            Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
            Buffer.BlockCopy(value, 0, result, prefix.Length, value.Length);
            return result;
        }

        private sealed class SignedFixture : IDisposable
        {
            private readonly RSA _signingKey;
            private readonly string _directory;

            private SignedFixture(
                RSA signingKey,
                string directory,
                string manifestText,
                byte[] assetBytes,
                string keyId)
            {
                _signingKey = signingKey;
                _directory = directory;
                ManifestText = manifestText;
                AssetBytes = assetBytes;
                ManifestPath = Path.Combine(directory, "release.manifest");
                SignaturePath = Path.Combine(directory, "release.manifest.sig");
                AssetPath = Path.Combine(directory, GitHubReleaseClient.AssetName);

                var publicParameters = signingKey.ExportParameters(false);
                Verifier = new ReleaseSignatureVerifier(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [keyId] = ToPublicKeyXml(publicParameters),
                    });
                WriteManifest(manifestText);
                WriteAsset(assetBytes);
            }

            public string ManifestPath { get; }

            public string SignaturePath { get; }

            public string AssetPath { get; }

            public string ManifestText { get; private set; }

            public byte[] ManifestBytes { get; private set; }

            public byte[] AssetBytes { get; }

            public string ManifestSha256 { get; private set; }

            public ReleaseSignatureVerifier Verifier { get; set; }

            public static SignedFixture Create(string keyId = "release-key-1")
            {
                var signingKey = RSA.Create(2048);
                var assetBytes = new byte[] { 1, 2, 3, 4, 5 };
                var hash = ToLowerHex(SHA256.HashData(assetBytes));
                var manifest = string.Join(
                    "\n",
                    "schema=1",
                    "keyId=" + keyId,
                    "tag=v1.2.3.4",
                    "version=1.2.3.4",
                    "assetName=" + GitHubReleaseClient.AssetName,
                    "size=" + assetBytes.Length,
                    "sha256=" + hash);
                return new SignedFixture(
                    signingKey,
                    Path.Combine(Path.GetTempPath(), "watchtogether-signed-release-" + Guid.NewGuid().ToString("N")),
                    manifest,
                    assetBytes,
                    keyId);
            }

            public void Verify()
            {
                Verifier.Verify(ManifestPath, SignaturePath, AssetPath);
            }

            public void WriteManifest(string manifestText)
            {
                WriteManifestBytes(Encoding.UTF8.GetBytes(manifestText));
                ManifestText = manifestText;
                ManifestBytes = Encoding.UTF8.GetBytes(manifestText);
                ManifestSha256 = GetValue(manifestText.Split('\n'), "sha256");
            }

            public void WriteManifestBytes(byte[] bytes)
            {
                Directory.CreateDirectory(_directory);
                File.WriteAllBytes(ManifestPath, bytes);
                ManifestBytes = bytes;
                WriteSignatureFor(bytes);
            }

            public void WriteSignature(byte[] signature)
            {
                Directory.CreateDirectory(_directory);
                File.WriteAllBytes(SignaturePath, signature);
            }

            public void WriteAsset(byte[] bytes)
            {
                Directory.CreateDirectory(_directory);
                File.WriteAllBytes(AssetPath, bytes);
            }

            public void Dispose()
            {
                _signingKey.Dispose();
                try
                {
                    if (Directory.Exists(_directory))
                    {
                        Directory.Delete(_directory, true);
                    }
                }
                catch
                {
                    // Best effort cleanup for test files.
                }
            }

            private void WriteSignatureFor(byte[] bytes)
            {
                WriteSignature(Encoding.ASCII.GetBytes(Convert.ToBase64String(
                    _signingKey.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))));
            }

            private static string GetValue(string[] lines, string key)
            {
                foreach (var line in lines)
                {
                    if (line.StartsWith(key + "=", StringComparison.Ordinal))
                    {
                        return line.Substring(key.Length + 1);
                    }
                }

                return null;
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
