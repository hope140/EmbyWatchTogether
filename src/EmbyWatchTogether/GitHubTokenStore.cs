using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Stores the optional GitHub API token outside of Emby's plugin
    /// configuration. The configuration endpoint returns and accepts the
    /// complete PluginConfiguration object, so a token must never be placed
    /// there.
    /// </summary>
    public sealed class GitHubTokenStore
    {
        private const int MaxTokenBytes = 512;
        private const string SecretsDirectoryName = "secrets";
        private const string TokenFileName = "github-token";
        private const int PrivateDirectoryMode = 448; // 0700
        private const int PrivateFileMode = 384; // 0600

        private readonly string _secretsDirectoryPath;
        private readonly string _tokenPath;

        public GitHubTokenStore(string dataFolderPath)
        {
            if (string.IsNullOrWhiteSpace(dataFolderPath))
            {
                throw new ArgumentException("data folder is required", nameof(dataFolderPath));
            }

            _secretsDirectoryPath = Path.Combine(dataFolderPath, SecretsDirectoryName);
            _tokenPath = Path.Combine(_secretsDirectoryPath, TokenFileName);
        }

        /// <summary>
        /// Returns the token, or null when no token has been configured.
        /// Untrusted or unreadable state throws so callers cannot silently
        /// fall back to an unauthenticated request.
        /// </summary>
        public string GetToken()
        {
            EnsurePrivateDirectory(createIfMissing: false);
            if (!EntryExistsOrReparsePoint(_tokenPath))
            {
                return null;
            }

            EnsureRegularFile(_tokenPath);
            EnsurePrivateFile(_tokenPath);

            try
            {
                var info = new FileInfo(_tokenPath);
                if (!info.Exists)
                {
                    throw new GitHubTokenStoreException("configured GitHub token cannot be read");
                }

                if (info.Length <= 0 || info.Length > MaxTokenBytes)
                {
                    throw new GitHubTokenStoreException("configured GitHub token is invalid");
                }

                byte[] bytes = File.ReadAllBytes(_tokenPath);
                if (bytes.Length <= 0 || bytes.Length > MaxTokenBytes)
                {
                    throw new GitHubTokenStoreException("configured GitHub token is invalid");
                }

                for (int i = 0; i < bytes.Length; i++)
                {
                    if (bytes[i] > 0x7f)
                    {
                        throw new GitHubTokenStoreException("configured GitHub token is invalid");
                    }
                }
                string token = Encoding.ASCII.GetString(bytes);
                if (!IsValidToken(token) || Encoding.ASCII.GetByteCount(token) != bytes.Length)
                {
                    throw new GitHubTokenStoreException("configured GitHub token is invalid");
                }

                return token;
            }
            catch (GitHubTokenStoreException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                throw new GitHubTokenStoreException("configured GitHub token cannot be read");
            }
        }

        public bool IsConfigured()
        {
            return GetToken() != null;
        }

        public void SetToken(string token)
        {
            if (!IsValidToken(token))
            {
                throw new ArgumentException("GitHub token must be non-empty ASCII without whitespace or control characters and no longer than 512 characters.", nameof(token));
            }

            EnsurePrivateDirectory(createIfMissing: true);
            // Validate an existing value before preparing its replacement. A
            // damaged or untrusted file must remain untouched until an
            // operator explicitly removes it.
            if (EntryExistsOrReparsePoint(_tokenPath))
            {
                GetToken();
            }
            string temporaryPath = null;
            try
            {
                temporaryPath = Path.Combine(
                    _secretsDirectoryPath,
                    TokenFileName + ".tmp-" + Guid.NewGuid().ToString("N"));

                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    byte[] bytes = Encoding.ASCII.GetBytes(token);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                EnsureRegularFile(temporaryPath);
                EnsurePrivateFile(temporaryPath);

                if (EntryExistsOrReparsePoint(_tokenPath))
                {
                    EnsureRegularFile(_tokenPath);
                    File.Replace(temporaryPath, _tokenPath, null);
                }
                else
                {
                    File.Move(temporaryPath, _tokenPath);
                }
                temporaryPath = null;
            }
            catch (GitHubTokenStoreException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                throw new GitHubTokenStoreException("configured GitHub token cannot be saved");
            }
            finally
            {
                if (!string.IsNullOrEmpty(temporaryPath))
                {
                    TryDeleteTemporaryFile(temporaryPath);
                }
            }
        }

        public bool Clear()
        {
            EnsurePrivateDirectory(createIfMissing: false);
            if (!EntryExistsOrReparsePoint(_tokenPath))
            {
                return false;
            }

            EnsureRegularFile(_tokenPath);
            EnsurePrivateFile(_tokenPath);
            try
            {
                File.Delete(_tokenPath);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                throw new GitHubTokenStoreException("configured GitHub token cannot be cleared");
            }
        }

        private void EnsurePrivateDirectory(bool createIfMissing)
        {
            if (!EntryExistsOrReparsePoint(_secretsDirectoryPath))
            {
                if (!createIfMissing)
                {
                    return;
                }

                try
                {
                    Directory.CreateDirectory(_secretsDirectoryPath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
                {
                    throw new GitHubTokenStoreException("token storage is unavailable");
                }
            }

            EnsureDirectoryIsNotReparsePoint(_secretsDirectoryPath);
            EnsurePrivateMode(_secretsDirectoryPath, PrivateDirectoryMode, "token storage permissions cannot be secured");
        }

        private static void EnsureRegularFile(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                {
                    throw new GitHubTokenStoreException("configured GitHub token is not a regular file");
                }
            }
            catch (GitHubTokenStoreException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FileNotFoundException)
            {
                throw new GitHubTokenStoreException("configured GitHub token cannot be inspected");
            }
        }

        private static void EnsureDirectoryIsNotReparsePoint(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Directory) == 0 ||
                    (attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new GitHubTokenStoreException("token storage is not a regular directory");
                }
            }
            catch (GitHubTokenStoreException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
            {
                throw new GitHubTokenStoreException("token storage cannot be inspected");
            }
        }

        private static bool EntryExistsOrReparsePoint(string path)
        {
            try
            {
                File.GetAttributes(path);
                return true;
            }
            catch (FileNotFoundException)
            {
                return HasDirectoryEntry(path);
            }
            catch (DirectoryNotFoundException)
            {
                return HasDirectoryEntry(path);
            }
            catch (UnauthorizedAccessException)
            {
                throw new GitHubTokenStoreException("token storage cannot be inspected");
            }
            catch (IOException)
            {
                throw new GitHubTokenStoreException("token storage cannot be inspected");
            }
        }

        private static bool HasDirectoryEntry(string path)
        {
            string parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                return false;
            }

            string name = Path.GetFileName(path);
            try
            {
                foreach (string entry in Directory.GetFileSystemEntries(parent))
                {
                    if (string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new GitHubTokenStoreException("token storage cannot be inspected");
            }
        }

        private static void EnsurePrivateFile(string path)
        {
            EnsurePrivateMode(path, PrivateFileMode, "configured GitHub token permissions cannot be secured");
        }

        private static void EnsurePrivateMode(string path, int mode, string error)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            if (NativeMethods.chmod(path, mode) != 0)
            {
                throw new GitHubTokenStoreException(error);
            }
        }

        private static bool IsValidToken(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > MaxTokenBytes)
            {
                return false;
            }

            foreach (char c in token)
            {
                if (c > 0x7f || char.IsWhiteSpace(c) || char.IsControl(c))
                {
                    return false;
                }
            }

            return true;
        }

        private static void TryDeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Do not expose token storage details through an API error.
            }
        }

        private static class NativeMethods
        {
            [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
            internal static extern int chmod(string path, int mode);
        }
    }

    public sealed class GitHubTokenStoreException : Exception
    {
        public GitHubTokenStoreException(string message)
            : base(message)
        {
        }

        public GitHubTokenStoreException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
