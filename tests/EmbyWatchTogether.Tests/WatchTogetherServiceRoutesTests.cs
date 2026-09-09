using System;
using System.Linq;
using MediaBrowser.Model.Services;
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
    }
}
