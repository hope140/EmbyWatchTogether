using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// The signed release manifest used to bind a downloaded plugin to a
    /// release tag, an expected file name and a content hash.
    /// </summary>
    public sealed class ReleaseManifest
    {
        public int Schema { get; internal set; }

        public string KeyId { get; internal set; }

        public string Tag { get; internal set; }

        public string Version { get; internal set; }

        public string AssetName { get; internal set; }

        public long Size { get; internal set; }

        public string Sha256 { get; internal set; }
    }

    /// <summary>
    /// Verifies a release manifest, its detached RSA signature and the
    /// downloaded plugin DLL without using the network or loading the DLL
    /// into memory.
    /// </summary>
    public sealed class ReleaseSignatureVerifier
    {
        public const long MaxManifestBytes = 16 * 1024;

        public const long MaxSignatureBytes = 8 * 1024;

        public const long MaxAssetBytes = 50 * 1024 * 1024;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private static readonly string[] ManifestFields =
        {
            "schema",
            "keyId",
            "tag",
            "version",
            "assetName",
            "size",
            "sha256",
        };

        private readonly IReadOnlyDictionary<string, string> _trustedPublicKeys;

        public ReleaseSignatureVerifier(IReadOnlyDictionary<string, string> trustedPublicKeys)
        {
            if (trustedPublicKeys == null)
            {
                throw new ArgumentNullException(nameof(trustedPublicKeys));
            }

            var copiedKeys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in trustedPublicKeys)
            {
                copiedKeys.Add(entry.Key, entry.Value);
            }

            _trustedPublicKeys = copiedKeys;
        }

        /// <summary>
        /// Verifies the supplied files and returns the authenticated manifest.
        /// The public key map contains keyId to XML RSA public key values. The
        /// private key is never needed by the plugin and must not be supplied.
        /// </summary>
        public ReleaseManifest Verify(
            string manifestPath,
            string signaturePath,
            string assetPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                throw new ArgumentNullException(nameof(manifestPath));
            }

            if (string.IsNullOrWhiteSpace(signaturePath))
            {
                throw new ArgumentNullException(nameof(signaturePath));
            }

            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw new ArgumentNullException(nameof(assetPath));
            }

            var manifestBytes = ReadBoundedFile(
                manifestPath,
                MaxManifestBytes,
                "发布清单");
            var signatureBytes = ReadBoundedFile(
                signaturePath,
                MaxSignatureBytes,
                "发布签名");
            var manifest = ParseManifest(manifestBytes);

            if (!_trustedPublicKeys.TryGetValue(manifest.KeyId, out var publicKeyXml) ||
                string.IsNullOrWhiteSpace(publicKeyXml))
            {
                throw new ReleaseValidationException("发布签名使用了未知的 keyId。");
            }

            var signature = DecodeSignature(signatureBytes);
            using (var rsa = CreatePublicKey(publicKeyXml))
            {
                bool signatureValid;
                try
                {
                    signatureValid = rsa.VerifyData(
                        manifestBytes,
                        signature,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1);
                }
                catch (CryptographicException ex)
                {
                    throw new ReleaseValidationException("发布清单签名验证失败。", ex);
                }

                if (!signatureValid)
                {
                    throw new ReleaseValidationException("发布清单签名验证失败。");
                }
            }

            VerifyAsset(assetPath, manifest);
            return manifest;
        }

        private static ReleaseManifest ParseManifest(byte[] manifestBytes)
        {
            if (manifestBytes == null || manifestBytes.Length == 0)
            {
                throw new ReleaseValidationException("发布清单为空。");
            }

            if (StartsWithUtf8Bom(manifestBytes))
            {
                throw new ReleaseValidationException("发布清单不得包含 UTF-8 BOM。");
            }

            string text;
            try
            {
                text = StrictUtf8.GetString(manifestBytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw new ReleaseValidationException("发布清单不是有效的 UTF-8 文本。", ex);
            }

            if (text.IndexOf('\r') >= 0)
            {
                throw new ReleaseValidationException("发布清单必须使用 LF 换行。");
            }

            var lines = text.Split(new[] { '\n' }, StringSplitOptions.None);
            if (lines.Length != ManifestFields.Length)
            {
                throw new ReleaseValidationException("发布清单字段数量不正确。");
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (string.IsNullOrEmpty(line) || line != line.Trim())
                {
                    throw new ReleaseValidationException("发布清单包含空行或额外空白。");
                }

                var separator = line.IndexOf('=');
                if (separator <= 0 || separator == line.Length - 1 ||
                    line.IndexOf('=', separator + 1) >= 0)
                {
                    throw new ReleaseValidationException("发布清单包含非法 key=value 字段。");
                }

                var key = line.Substring(0, separator);
                var value = line.Substring(separator + 1);
                if (!string.Equals(key, ManifestFields[index], StringComparison.Ordinal) ||
                    values.ContainsKey(key) ||
                    value.IndexOfAny(new[] { ' ', '\t' }) >= 0)
                {
                    throw new ReleaseValidationException("发布清单包含重复、未知或非 canonical 字段。");
                }

                values.Add(key, value);
            }

            if (!string.Equals(values["schema"], "1", StringComparison.Ordinal))
            {
                throw new ReleaseValidationException("发布清单 schema 不受支持。");
            }

            var keyId = values["keyId"];
            if (!IsSafeKeyId(keyId))
            {
                throw new ReleaseValidationException("发布清单 keyId 无效。");
            }

            var version = values["version"];
            if (!TryParseStrictVersion(version, out _))
            {
                throw new ReleaseValidationException("发布清单 version 无效。");
            }

            var tag = values["tag"];
            if (!tag.StartsWith("v", StringComparison.Ordinal) ||
                !string.Equals(tag.Substring(1), version, StringComparison.Ordinal))
            {
                throw new ReleaseValidationException("发布清单 tag 与 version 不一致。");
            }

            if (!string.Equals(values["assetName"], GitHubReleaseClient.AssetName, StringComparison.Ordinal) ||
                values["assetName"].IndexOfAny(new[] { '/', '\\' }) >= 0)
            {
                throw new ReleaseValidationException("发布清单 assetName 无效。");
            }

            if (!long.TryParse(
                    values["size"],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var size) ||
                size < 0 || size > MaxAssetBytes)
            {
                throw new ReleaseValidationException("发布清单 size 超出允许范围。");
            }

            var sha256 = values["sha256"];
            if (!IsLowerHexSha256(sha256))
            {
                throw new ReleaseValidationException("发布清单 sha256 无效。");
            }

            return new ReleaseManifest
            {
                Schema = 1,
                KeyId = keyId,
                Tag = tag,
                Version = version,
                AssetName = values["assetName"],
                Size = size,
                Sha256 = sha256,
            };
        }

        private static byte[] DecodeSignature(byte[] signatureBytes)
        {
            string text;
            try
            {
                text = StrictUtf8.GetString(signatureBytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw new ReleaseValidationException("发布签名不是有效的 UTF-8 文本。", ex);
            }

            if (string.IsNullOrEmpty(text) || text.Length % 4 != 0)
            {
                throw new ReleaseValidationException("发布签名不是有效的 base64 文本。");
            }

            for (var index = 0; index < text.Length; index++)
            {
                var value = text[index];
                if (!((value >= 'A' && value <= 'Z') ||
                      (value >= 'a' && value <= 'z') ||
                      (value >= '0' && value <= '9') ||
                      value == '+' || value == '/' || value == '='))
                {
                    throw new ReleaseValidationException("发布签名不是有效的 base64 文本。");
                }
            }

            try
            {
                return Convert.FromBase64String(text);
            }
            catch (FormatException ex)
            {
                throw new ReleaseValidationException("发布签名不是有效的 base64 文本。", ex);
            }
        }

        private static RSA CreatePublicKey(string publicKeyXml)
        {
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                };
                XDocument document;
                using (var reader = XmlReader.Create(new StringReader(publicKeyXml), settings))
                {
                    document = XDocument.Load(reader, LoadOptions.None);
                }

                var root = document.Root;
                var modulus = root == null ? null : root.Element("Modulus");
                var exponent = root == null ? null : root.Element("Exponent");
                if (root == null || !string.Equals(root.Name.LocalName, "RSAKeyValue", StringComparison.Ordinal) ||
                    modulus == null || exponent == null ||
                    string.IsNullOrWhiteSpace(modulus.Value) ||
                    string.IsNullOrWhiteSpace(exponent.Value))
                {
                    throw new FormatException("RSA 公钥格式不受支持。");
                }

                var parameters = new RSAParameters
                {
                    Modulus = Convert.FromBase64String(modulus.Value),
                    Exponent = Convert.FromBase64String(exponent.Value),
                };
                var rsa = RSA.Create();
                rsa.ImportParameters(parameters);
                return rsa;
            }
            catch (Exception ex) when (
                ex is CryptographicException ||
                ex is FormatException ||
                ex is XmlException ||
                ex is InvalidOperationException)
            {
                throw new ReleaseValidationException("受信任 RSA 公钥无效。", ex);
            }
        }

        private static void VerifyAsset(string assetPath, ReleaseManifest manifest)
        {
            var fileInfo = new FileInfo(assetPath);
            if (!fileInfo.Exists)
            {
                throw new ReleaseValidationException("正式版插件 DLL 不存在。");
            }

            if (fileInfo.Length > MaxAssetBytes)
            {
                throw new ReleaseValidationException("正式版插件 DLL 超出大小限制。");
            }

            if (fileInfo.Length != manifest.Size)
            {
                throw new ReleaseValidationException("正式版插件 DLL 大小与发布清单不一致。");
            }

            string actualHash;
            try
            {
                using (var stream = new FileStream(
                           assetPath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read))
                using (var sha256 = SHA256.Create())
                {
                    if (stream.Length != manifest.Size || stream.Length > MaxAssetBytes)
                    {
                        throw new ReleaseValidationException("正式版插件 DLL 大小在读取期间发生变化。");
                    }

                    actualHash = ToLowerHex(sha256.ComputeHash(stream));
                }
            }
            catch (ReleaseValidationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ReleaseValidationException("无法读取正式版插件 DLL。", ex);
            }

            if (!FixedTimeEquals(actualHash, manifest.Sha256))
            {
                throw new ReleaseValidationException("正式版插件 DLL SHA-256 校验失败。");
            }
        }

        private static byte[] ReadBoundedFile(string path, long maxBytes, string description)
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                throw new ReleaseValidationException(description + "文件不存在。");
            }

            if (fileInfo.Length > maxBytes)
            {
                throw new ReleaseValidationException(description + "超过大小限制。");
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var buffer = new MemoryStream())
                {
                    var bytes = new byte[4096];
                    int read;
                    while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
                    {
                        buffer.Write(bytes, 0, read);
                        if (buffer.Length > maxBytes)
                        {
                            throw new ReleaseValidationException(description + "超过大小限制。");
                        }
                    }

                    return buffer.ToArray();
                }
            }
            catch (ReleaseValidationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ReleaseValidationException("无法读取" + description + "文件。", ex);
            }
        }

        private static bool IsSafeKeyId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64)
            {
                return false;
            }

            foreach (var character in value)
            {
                if (!((character >= 'A' && character <= 'Z') ||
                      (character >= 'a' && character <= 'z') ||
                      (character >= '0' && character <= '9') ||
                      character == '-' || character == '_' || character == '.'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryParseStrictVersion(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var parts = value.Split('.');
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

            return Version.TryParse(value, out version);
        }

        private static bool IsLowerHexSha256(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }

            foreach (var character in value)
            {
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool StartsWithUtf8Bom(byte[] bytes)
        {
            return bytes.Length >= 3 &&
                bytes[0] == 0xEF &&
                bytes[1] == 0xBB &&
                bytes[2] == 0xBF;
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            var difference = left.Length ^ right.Length;
            var length = Math.Max(left.Length, right.Length);
            for (var index = 0; index < length; index++)
            {
                var leftValue = index < left.Length ? left[index] : '\0';
                var rightValue = index < right.Length ? right[index] : '\0';
                difference |= leftValue ^ rightValue;
            }

            return difference == 0;
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
