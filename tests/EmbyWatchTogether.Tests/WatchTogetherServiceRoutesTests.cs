using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Users;
using Moq;
using Xunit;

namespace Emby.Plugins.WatchTogether.Tests
{
    public class WatchTogetherServiceRoutesTests
    {
        [Theory]
        [InlineData(typeof(GetRoomsRequest), "/WatchTogether/Rooms", "GET")]
        [InlineData(typeof(CreateRoomRequest), "/WatchTogether/Rooms", "POST")]
        [InlineData(typeof(CreateInvitationRequest), "/WatchTogether/Invitations", "POST")]
        [InlineData(typeof(GetInvitationsRequest), "/WatchTogether/Invitations", "GET")]
        [InlineData(typeof(DeleteInvitationRequest), "/WatchTogether/Invitations/{Id}", "DELETE")]
        [InlineData(typeof(AcceptInvitationRequest), "/WatchTogether/Invitations/{Code}/Accept", "POST")]
        [InlineData(typeof(DeleteRoomRequest), "/WatchTogether/Rooms/{Id}", "DELETE")]
        [InlineData(typeof(ControlRoomRequest), "/WatchTogether/Rooms/{Id}/Action", "POST")]
        [InlineData(typeof(GetRoomStateRequest), "/WatchTogether/Rooms/{Id}/State", "GET")]
        [InlineData(typeof(JoinRoomRequest), "/WatchTogether/Rooms/{Id}/Join", "POST")]
        [InlineData(typeof(LeaveRoomRequest), "/WatchTogether/Rooms/{Id}/Leave", "POST")]
        [InlineData(typeof(SendRoomMessageRequest), "/WatchTogether/Rooms/{Id}/Message", "POST")]
        [InlineData(typeof(GetUsersRequest), "/WatchTogether/Users", "GET")]
        [InlineData(typeof(GetPluginInfoRequest), "/WatchTogether/Info", "GET")]
        [InlineData(typeof(GetGitHubTokenRequest), "/WatchTogether/GitHubToken", "GET")]
        [InlineData(typeof(SetGitHubTokenRequest), "/WatchTogether/GitHubToken", "POST")]
        [InlineData(typeof(DeleteGitHubTokenRequest), "/WatchTogether/GitHubToken", "DELETE")]
        public void RequestDto_DeclaresExpectedRoute(Type dto, string path, string verbs)
        {
            var route = (RouteAttribute)dto.GetCustomAttributes(typeof(RouteAttribute), false).Single();

            Assert.Equal(path, route.Path);
            Assert.Equal(verbs, route.Verbs);
        }

        [Fact]
        public void Service_ImplementsIService()
        {
            Assert.Contains(typeof(MediaBrowser.Model.Services.IService),
                typeof(WatchTogetherService).GetInterfaces());
        }

        [Fact]
        public void GitHubTokenRoutesRequireAdministratorAndNeverReturnToken()
        {
            string root = Path.Combine(Path.GetTempPath(), "watch-together-route-tests", Guid.NewGuid().ToString("N"));
            try
            {
                var plugin = CreatePlugin(root);
                var admin = CreateService(true);
                var member = CreateService(false);
                SetRuntimePlugin(admin, plugin);
                SetRuntimePlugin(member, plugin);

                Assert.Throws<UnauthorizedAccessException>(() => member.Get(new GetGitHubTokenRequest()));
                Assert.Throws<UnauthorizedAccessException>(() => member.Post(new SetGitHubTokenRequest { Token = "ghp_private" }));
                Assert.Throws<UnauthorizedAccessException>(() => member.Delete(new DeleteGitHubTokenRequest()));

                var empty = admin.Get(new GetGitHubTokenRequest());
                Assert.False(GetBoolean(empty, "Configured"));

                var saved = admin.Post(new SetGitHubTokenRequest { Token = "ghp_private" });
                Assert.True(GetBoolean(saved, "Configured"));
                Assert.DoesNotContain("ghp_private", saved.ToString(), StringComparison.Ordinal);

                var status = admin.Get(new GetGitHubTokenRequest());
                Assert.True(GetBoolean(status, "Configured"));
                Assert.DoesNotContain("ghp_private", status.ToString(), StringComparison.Ordinal);

                var removed = admin.Delete(new DeleteGitHubTokenRequest());
                Assert.False(GetBoolean(removed, "Configured"));
                Assert.False(GetBoolean(admin.Get(new GetGitHubTokenRequest()), "Configured"));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                SetPluginInstance(null);
            }
        }

        private static Plugin CreatePlugin(string root)
        {
            var paths = new Mock<IApplicationPaths>();
            paths.SetupGet(p => p.PluginConfigurationsPath).Returns(root);
            var serializer = new Mock<IXmlSerializer>();
            var plugin = new Plugin(paths.Object, serializer.Object);
            plugin.SetAttributes(Path.Combine(root, "plugin.dll"), root, new Version(1, 0, 0, 0));
            return plugin;
        }

        private static WatchTogetherService CreateService(bool administrator)
        {
#pragma warning disable SYSLIB0050
            var user = (User)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(User));
#pragma warning restore SYSLIB0050
            user.Id = Guid.NewGuid();
            user.Policy = new UserPolicy { IsAdministrator = administrator };
            var auth = new Mock<IAuthorizationContext>();
            auth.Setup(c => c.GetAuthorizationInfo(It.IsAny<IRequest>()))
                .Returns(new AuthorizationInfo { User = user });
            return new WatchTogetherService { AuthorizationContext = auth.Object };
        }

        private static bool GetBoolean(object value, string property)
        {
            return (bool)value.GetType().GetProperty(property).GetValue(value);
        }

        private static void SetRuntimePlugin(WatchTogetherService service, Plugin plugin)
        {
            typeof(WatchTogetherService).GetProperty("RuntimePlugin", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(service, plugin);
        }

        private static void SetPluginInstance(Plugin value)
        {
            typeof(Plugin).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(null, value);
        }
    }
}
