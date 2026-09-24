using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public sealed class GitHubTokenStoreTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "watch-together-token-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [Fact]
        public void MissingStoreIsNotConfiguredAndDoesNotCreateSecretsDirectory()
        {
            var store = new GitHubTokenStore(_root);

            Assert.False(store.IsConfigured());
            Assert.False(Directory.Exists(Path.Combine(_root, "secrets")));
        }

        [Fact]
        public void SetGetReplaceAndClearUseOnlyTheFixedTokenFile()
        {
            var store = new GitHubTokenStore(_root);

            store.SetToken("ghp_first_token");
            Assert.True(store.IsConfigured());
            Assert.Equal("ghp_first_token", store.GetToken());
            Assert.Equal(
                "ghp_first_token",
                File.ReadAllText(Path.Combine(_root, "secrets", "github-token")));

            store.SetToken("ghp_replaced_token");
            Assert.Equal("ghp_replaced_token", store.GetToken());
            Assert.Empty(Directory.GetFiles(Path.Combine(_root, "secrets"), "*.bak"));
            Assert.Empty(Directory.GetFiles(Path.Combine(_root, "secrets"), "*.tmp-*"));

            Assert.True(store.Clear());
            Assert.False(store.IsConfigured());
            Assert.False(store.Clear());
            Assert.Empty(Directory.GetFiles(Path.Combine(_root, "secrets")));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("token with spaces")]
        [InlineData("token\nwith-newline")]
        [InlineData("token\twith-tab")]
        [InlineData("token\u0001with-control")]
        [InlineData("令牌")]
        public void SetRejectsInvalidToken(string token)
        {
            var store = new GitHubTokenStore(_root);

            Assert.Throws<ArgumentException>(() => store.SetToken(token));
            Assert.False(Directory.Exists(Path.Combine(_root, "secrets")));
        }

        [Fact]
        public void SetRejectsOversizedToken()
        {
            var store = new GitHubTokenStore(_root);

            Assert.Throws<ArgumentException>(() => store.SetToken(new string('a', 513)));
        }

        [Fact]
        public void CorruptExistingValueFailsClosedAndIsNotOverwritten()
        {
            var store = new GitHubTokenStore(_root);
            store.SetToken("ghp_original");
            string tokenPath = Path.Combine(_root, "secrets", "github-token");
            File.WriteAllText(tokenPath, "bad token\n");

            Assert.Throws<GitHubTokenStoreException>(() => store.IsConfigured());
            Assert.Throws<GitHubTokenStoreException>(() => store.SetToken("ghp_new"));
            Assert.Equal("bad token\n", File.ReadAllText(tokenPath));
            Assert.Empty(Directory.GetFiles(Path.Combine(_root, "secrets"), "*.bak"));
            Assert.Empty(Directory.GetFiles(Path.Combine(_root, "secrets"), "*.tmp-*"));
        }

        [Fact]
        public void NonAsciiBytesFailClosedAndAreNotOverwritten()
        {
            var store = new GitHubTokenStore(_root);
            store.SetToken("ghp_original");
            string tokenPath = Path.Combine(_root, "secrets", "github-token");
            File.WriteAllBytes(tokenPath, new byte[] { 0x67, 0x68, 0x80 });

            Assert.Throws<GitHubTokenStoreException>(() => store.GetToken());
            Assert.Throws<GitHubTokenStoreException>(() => store.SetToken("ghp_new"));
            Assert.Equal(new byte[] { 0x67, 0x68, 0x80 }, File.ReadAllBytes(tokenPath));
        }

        [Fact]
        public void DirectorySymlinkIsRejectedWhenThePlatformAllowsCreatingOne()
        {
            string target = Path.Combine(_root, "secrets-target");
            string link = Path.Combine(_root, "secrets");
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(target);
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is PlatformNotSupportedException)
            {
                return;
            }

            var store = new GitHubTokenStore(_root);
            Assert.Throws<GitHubTokenStoreException>(() => store.IsConfigured());
            Assert.Throws<GitHubTokenStoreException>(() => store.SetToken("ghp_link_target"));
        }

        [Fact]
        public void FileSymlinkIsRejectedWhenThePlatformAllowsCreatingOne()
        {
            var store = new GitHubTokenStore(_root);
            store.SetToken("ghp_original");
            string tokenPath = Path.Combine(_root, "secrets", "github-token");
            string targetPath = Path.Combine(_root, "other-token");
            File.WriteAllText(targetPath, "ghp_other");
            File.Delete(tokenPath);
            try
            {
                File.CreateSymbolicLink(tokenPath, targetPath);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is PlatformNotSupportedException)
            {
                return;
            }

            Assert.Throws<GitHubTokenStoreException>(() => store.GetToken());
            Assert.Throws<GitHubTokenStoreException>(() => store.SetToken("ghp_new"));
        }

        [Fact]
        public void TokenStatusNeverReturnsTheToken()
        {
            var store = new GitHubTokenStore(_root);
            store.SetToken("ghp_private_value");

            Assert.True(store.IsConfigured());
            Assert.DoesNotContain("ghp_private_value", store.IsConfigured().ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void LinuxStoragePermissionsArePrivateWhenSupported()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            var store = new GitHubTokenStore(_root);
            store.SetToken("ghp_permission_test");
            var directoryMode = File.GetUnixFileMode(Path.Combine(_root, "secrets"));
            var fileMode = File.GetUnixFileMode(Path.Combine(_root, "secrets", "github-token"));

            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                directoryMode);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, fileMode);
        }
    }
}
