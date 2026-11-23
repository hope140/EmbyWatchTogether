using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Reflection;
using System.Text.Json;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Users;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class NotificationLanguageIntegrationTests
    {
        [Theory]
        [InlineData("ja")]
        [InlineData("ko")]
        [InlineData("ru")]
        [InlineData("fr")]
        [InlineData("de")]
        [InlineData("es")]
        [InlineData("pt")]
        public void PreferenceEndpointUsesAuthenticatedIdentityOnlyAndSurvivesReload(string additionalLanguage)
        {
            var root = Path.Combine(Path.GetTempPath(), "wt-pref-" + Guid.NewGuid().ToString("N"));
            try
            {
                var serializer = Serializer();
#pragma warning disable SYSLIB0050
                var plugin = (Plugin)FormatterServices.GetUninitializedObject(typeof(Plugin));
#pragma warning restore SYSLIB0050
                SetProperty(plugin, "Rooms", new RoomManager(new RoomStore(Path.Combine(root, "rooms.json"), serializer)));
                var path = Path.Combine(root, "language.json");
                SetProperty(plugin, "NotificationLanguages", new NotificationLanguageStore(path, serializer));
                var first = Guid.NewGuid(); var second = Guid.NewGuid(); var third = Guid.NewGuid();
                var a = Service(plugin, first); var b = Service(plugin, second);
                a.Post(new SetNotificationLanguageRequest { Language = "en" });
                b.Post(new SetNotificationLanguageRequest { Language = "zh-Hant" });
                Service(plugin, third).Post(new SetNotificationLanguageRequest { Language = additionalLanguage });
                var loaded = new NotificationLanguageStore(path, serializer);
                Assert.Equal("en", loaded.GetLanguage(first.ToString("N")));
                Assert.Equal("zh-Hant", loaded.GetLanguage(second.ToString("N")));
                Assert.Equal(additionalLanguage, loaded.GetLanguage(third.ToString("N")));
                Assert.Throws<ArgumentException>(() => a.Post(new SetNotificationLanguageRequest { Language = "nl" }));
                Assert.Throws<UnauthorizedAccessException>(() => Service(plugin, null).Post(new SetNotificationLanguageRequest { Language = "en" }));
                Assert.Equal("en", loaded.GetLanguage(first.ToString("N")));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void FixedNoticesUseEachRecipientPreferenceAndFailSafely()
        {
            Func<string, string> preferences = id => id == "a" ? "en" : "zh";
            const string notice = "双方打开了不同视频，已暂停同步；请打开同一视频";
            Assert.Contains("Different videos", Translate(notice, "a", preferences));
            Assert.Equal(notice, Translate(notice, "b", preferences));
            Assert.Contains("雙方開啟", Translate(notice, "c", _ => "zh-Hant"));
            Assert.Equal(notice, Translate(notice, "a", _ => throw new IOException()));
            Assert.Equal("custom text", Translate("custom text", "a", preferences));
        }

        [Fact]
        public void LocalizedFixedNoticesCoverEverySystemMessage()
        {
            var type = typeof(Plugin).Assembly.GetType("Emby.Plugins.WatchTogether.NotificationText");
            var english = (IReadOnlyDictionary<string, string>)type.GetField("English", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            foreach (var language in new[] { "ja", "ko", "ru", "fr", "de", "es", "pt" })
            {
                foreach (var source in english.Keys)
                {
                    var localized = Translate(source, "recipient", _ => language);
                    Assert.False(string.IsNullOrWhiteSpace(localized));
                    Assert.NotEqual(source, localized);
                }
            }
        }

        private static WatchTogetherService Service(Plugin plugin, Guid? id)
        {
            User user = null;
            if (id.HasValue)
            {
#pragma warning disable SYSLIB0050
                user = (User)FormatterServices.GetUninitializedObject(typeof(User));
#pragma warning restore SYSLIB0050
                user.Id = id.Value;
                user.Policy = new UserPolicy { IsAdministrator = false };
            }
            var auth = new Mock<IAuthorizationContext>();
            auth.Setup(x => x.GetAuthorizationInfo(It.IsAny<IRequest>())).Returns(new AuthorizationInfo { User = user });
            var service = new WatchTogetherService { AuthorizationContext = auth.Object };
            SetProperty(service, "RuntimePlugin", plugin);
            return service;
        }

        private static void SetProperty(object target, string name, object value) =>
            target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);

        private static string Translate(string text, string user, Func<string, string> lookup)
        {
            var type = typeof(Plugin).Assembly.GetType("Emby.Plugins.WatchTogether.NotificationText");
            return (string)type.GetMethod("Translate").Invoke(null, new object[] { text, user, lookup });
        }

        private static IJsonSerializer Serializer()
        {
            var mock = new Mock<IJsonSerializer>();
            mock.Setup(x => x.SerializeToString(It.IsAny<object>())).Returns<object>(x => JsonSerializer.Serialize(x));
            mock.Setup(x => x.DeserializeFromString<List<NotificationLanguageEntry>>(It.IsAny<string>()))
                .Returns<string>(s => JsonSerializer.Deserialize<List<NotificationLanguageEntry>>(s));
            return mock.Object;
        }
    }
}
