using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Model.Serialization;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class NotificationLanguageStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "wt-language-" + Guid.NewGuid().ToString("N"));
        private string FilePath => Path.Combine(_dir, "languages.json");

        [Fact]
        public void DefaultsIsolationAndReload()
        {
            var store = new NotificationLanguageStore(FilePath, Serializer().Object);
            Assert.Equal("zh", store.GetLanguage(null));
            Assert.Equal("zh", store.GetLanguage("other"));
            store.SetLanguage("one", "en");
            store.SetLanguage("two", "zh");
            Assert.Equal("en", store.GetLanguage("ONE"));
            Assert.Equal("zh", store.GetLanguage("two"));
            var reloaded = new NotificationLanguageStore(FilePath, Serializer().Object);
            Assert.Equal("en", reloaded.GetLanguage("one"));
            Assert.Equal("zh", reloaded.GetLanguage("two"));
            reloaded.SetLanguage("one", "zh");
            Assert.Equal("zh", new NotificationLanguageStore(FilePath, Serializer().Object).GetLanguage("one"));
        }

        private sealed class LegacyEntry { public string UserId { get; set; } public string Language { get; set; } }

        [Fact]
        public void TraditionalPreferenceUsesLegacyReadableLanguageAndSurvivesReload()
        {
            var store = new NotificationLanguageStore(FilePath, Serializer().Object);
            store.SetLanguage("one", "zh-Hant");
            var raw = JsonSerializer.Deserialize<List<NotificationLanguageEntry>>(File.ReadAllText(FilePath));
            Assert.Equal("zh", raw.Single().Language);
            Assert.Equal("Hant", raw.Single().Script);
            var legacy = JsonSerializer.Deserialize<List<LegacyEntry>>(File.ReadAllText(FilePath));
            Assert.Equal("zh", legacy.Single().Language);
            Assert.Equal("zh-Hant", new NotificationLanguageStore(FilePath, Serializer().Object).GetLanguage("one"));
        }

        [Fact]
        public void InvalidInputIsRejectedWithoutWriting()
        {
            var store = new NotificationLanguageStore(FilePath, Serializer().Object);
            Assert.Throws<ArgumentException>(() => store.SetLanguage(null, "en"));
            foreach (var value in new[] { "", "EN", "en-US", "es", null })
                Assert.Throws<ArgumentException>(() => store.SetLanguage("one", value));
            Assert.False(File.Exists(FilePath));
        }

        [Fact]
        public async Task ConcurrentUpdatesDoNotLoseUsers()
        {
            var store = new NotificationLanguageStore(FilePath, Serializer().Object);
            await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(() => store.SetLanguage("user-" + i, "en"))));
            var reloaded = new NotificationLanguageStore(FilePath, Serializer().Object);
            foreach (var i in Enumerable.Range(0, 40)) Assert.Equal("en", reloaded.GetLanguage("user-" + i));
        }

        [Theory]
        [InlineData("{bad")]
        [InlineData("null")]
        [InlineData("[{\"UserId\":\"u\",\"Language\":\"es\"}]")]
        [InlineData("[{\"UserId\":\"u\",\"Language\":\"en\"},{\"UserId\":\"U\",\"Language\":\"zh\"}]")]
        public void CorruptDataFallsBackAndIsNotOverwritten(string contents)
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(FilePath, contents);
            var store = new NotificationLanguageStore(FilePath, Serializer().Object);
            Assert.Equal("zh", store.GetLanguage("u"));
            Assert.Throws<InvalidDataException>(() => store.SetLanguage("u", "en"));
            Assert.Equal(contents, File.ReadAllText(FilePath));
        }

        [Fact]
        public void FailedWriteLeavesPublishedAndPersistedStateUnchanged()
        {
            var serializer = Serializer();
            var store = new NotificationLanguageStore(FilePath, serializer.Object);
            store.SetLanguage("one", "en");
            var before = File.ReadAllText(FilePath);
            serializer.Setup(s => s.SerializeToString(It.IsAny<object>())).Throws(new IOException("disk full"));
            Assert.Throws<IOException>(() => store.SetLanguage("one", "zh"));
            Assert.Equal("en", store.GetLanguage("one"));
            Assert.Equal(before, File.ReadAllText(FilePath));
        }

        [Fact]
        public void FileSystemWriteFailureDoesNotPublishCandidate()
        {
            Directory.CreateDirectory(_dir);
            var blocker = Path.Combine(_dir, "not-a-directory");
            File.WriteAllText(blocker, "unchanged");
            var store = new NotificationLanguageStore(Path.Combine(blocker, "languages.json"), Serializer().Object);
            Assert.ThrowsAny<IOException>(() => store.SetLanguage("one", "en"));
            Assert.Equal("zh", store.GetLanguage("one"));
            Assert.Equal("unchanged", File.ReadAllText(blocker));
        }

        private static Mock<IJsonSerializer> Serializer()
        {
            var mock = new Mock<IJsonSerializer>();
            mock.Setup(s => s.SerializeToString(It.IsAny<object>())).Returns<object>(x => JsonSerializer.Serialize(x));
            mock.Setup(s => s.DeserializeFromString<List<NotificationLanguageEntry>>(It.IsAny<string>()))
                .Returns<string>(s => JsonSerializer.Deserialize<List<NotificationLanguageEntry>>(s));
            return mock;
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }
}
